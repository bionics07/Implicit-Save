using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ImplicitSave.Serialization
{
    /// <summary>
    /// The single description of what Unity's serializer persists. Both the Newtonsoft contract
    /// resolver and the editor validator read the rules from here.
    /// </summary>
    /// <remarks>
    /// There must be exactly one copy of these rules. If the resolver and the validator each grew
    /// their own, they would drift, and the drift would show up as the editor window and the save
    /// file disagreeing - the worst bug a save system can have.
    /// <para>
    /// The rules below were verified against Unity's own serializer rather than taken from the
    /// documentation: a public field, a private field with <c>[SerializeField]</c> and a
    /// <c>List&lt;T&gt;</c> are persisted; a plain private field, a <c>readonly</c> field, a static
    /// field, anything <c>[NonSerialized]</c>, every property and every <c>Dictionary</c> are not.
    /// </para>
    /// </remarks>
    public static class UnitySerializationRules
    {
        private const BindingFlags FieldLookup =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>
        /// How deep Unity will follow plain nested classes before it silently stops. Going past this
        /// loses data without a single error, which is why the validator refuses to stay quiet
        /// about it.
        /// </summary>
        public const int MaxNestingDepth = 7;

        /// <summary>
        /// Returns the fields Unity serializes for <paramref name="type"/>, base classes first, in
        /// declaration order - the same order Unity itself uses.
        /// </summary>
        public static IReadOnlyList<FieldInfo> GetSerializedFields(Type type)
        {
            var fields = new List<FieldInfo>();
            CollectSerializedFields(type, fields);
            return fields;
        }

        private static void CollectSerializedFields(Type type, List<FieldInfo> fields)
        {
            if (type == null || type == typeof(object) || type == typeof(SaveData))
            {
                return;
            }

            // Base first: Unity lays out inherited fields before the ones declared here.
            CollectSerializedFields(type.BaseType, fields);

            foreach (var field in type.GetFields(FieldLookup))
            {
                if (IsSerializedByUnity(field))
                {
                    fields.Add(field);
                }
            }
        }

        /// <summary>Whether Unity would persist this field.</summary>
        public static bool IsSerializedByUnity(FieldInfo field)
        {
            return IsSerializedByUnity(field, out _);
        }

        /// <summary>
        /// Whether Unity would persist this field, and if not, why - the reason is what the
        /// validator shows the user.
        /// </summary>
        public static bool IsSerializedByUnity(FieldInfo field, out string reason)
        {
            if (field == null)
            {
                throw new ArgumentNullException(nameof(field));
            }

            if (field.IsStatic)
            {
                reason = "it is static";
                return false;
            }

            if (field.IsInitOnly)
            {
                reason = "it is readonly";
                return false;
            }

            if (IsDefined(field, typeof(NonSerializedAttribute)))
            {
                reason = "it is marked [NonSerialized]";
                return false;
            }

            if (field.IsNotSerialized)
            {
                reason = "it is marked [NonSerialized]";
                return false;
            }

            var isPublic = field.IsPublic;
            var hasSerializeField = IsDefined(field, typeof(SerializeField));

            if (!isPublic && !hasSerializeField)
            {
                reason = "it is not public and has no [SerializeField]";
                return false;
            }

            // A Dictionary is the one type where being public is not enough: Unity 6.6 serializes it
            // only when the field carries [SerializeField]. Mirroring that exactly is what keeps the
            // window and the file from disagreeing.
            if (NativeDictionaries && IsPlainDictionary(field.FieldType) && !hasSerializeField)
            {
                reason = "a Dictionary is only serialized when the field has [SerializeField], " +
                         "even when the field is public";
                return false;
            }

            // [SerializeReference] changes what the type rules allow, so it has to be read here and
            // carried down rather than checked at the leaf.
            var byReference = IsDefined(field, typeof(SerializeReference));

            if (!IsSerializableType(field.FieldType, allowContainer: true, byReference: byReference, reason: out var typeReason))
            {
                reason = typeReason;
                return false;
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// The structs Unity serializes in native code, without carrying <c>[Serializable]</c>.
        /// </summary>
        /// <remarks>
        /// Asking for the attribute is how this package decides what Unity will keep - and for these
        /// types the attribute is simply not there. Unity's serializer knows them by name, from C++,
        /// so <c>Attribute.IsDefined(typeof(Vector3), ...)</c> is <c>false</c> while the Inspector
        /// shows the field and saves it.
        /// <para>
        /// Getting this wrong is expensive and quiet: without the list, a save holding a position or
        /// a colour shows the value in the editor window and writes a file that does not contain the
        /// field at all. The window and the disk telling different stories is the failure this whole
        /// contract exists to prevent, and it went unnoticed until warnings were read rather than
        /// errors.
        /// </para>
        /// <para>
        /// A list, not a namespace check: <c>UnityEngine</c> is full of types that must NOT be
        /// storable, and every entry here was confirmed against Unity's own serializer rather than
        /// assumed.
        /// </para>
        /// </remarks>
        internal static bool IsBuiltInStruct(Type type)
        {
            return BuiltInStructs.Contains(type);
        }

        private static readonly HashSet<Type> BuiltInStructs = new HashSet<Type>
        {
            typeof(UnityEngine.Vector2),
            typeof(UnityEngine.Vector3),
            typeof(UnityEngine.Vector4),
            typeof(UnityEngine.Vector2Int),
            typeof(UnityEngine.Vector3Int),
            typeof(UnityEngine.Quaternion),
            typeof(UnityEngine.Color),
            typeof(UnityEngine.Color32),
            typeof(UnityEngine.Rect),
            typeof(UnityEngine.RectInt),
            typeof(UnityEngine.Bounds),
            typeof(UnityEngine.BoundsInt),
            typeof(UnityEngine.LayerMask),
            typeof(UnityEngine.Matrix4x4),
            typeof(UnityEngine.Hash128)
        };

        /// <summary>
        /// Unity types this package deliberately does not store, and what to do instead.
        /// </summary>
        /// <remarks>
        /// Unity serializes these, so "it has no [Serializable]" would read as a mistake on their
        /// part rather than a decision on ours. They are authored content - a curve or a gradient is
        /// something a designer draws, not something a player accumulates - so the useful answer is
        /// to keep them on an asset and save a reference to it.
        /// </remarks>
        private static readonly Dictionary<Type, string> Unsupported = new Dictionary<Type, string>
        {
            {
                typeof(UnityEngine.AnimationCurve),
                "Unity serializes AnimationCurve, but ImplicitSave does not store it - a curve is authored " +
                "content, not player progress. Keep it on a ScriptableObject and save an id that points at it"
            },
            {
                typeof(UnityEngine.Gradient),
                "Unity serializes Gradient, but ImplicitSave does not store it - a gradient is authored " +
                "content, not player progress. Keep it on a ScriptableObject and save an id that points at it"
            }
        };

        /// <summary>An array or List, the only two collections Unity serializes.</summary>
        private static bool IsUnityContainer(Type type)
        {
            return type != null
                   && (type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)));
        }

        /// <summary>The leaf rules for a <c>[SerializeReference]</c> value.</summary>
        private static bool IsByReferenceType(Type type, out string reason)
        {
            reason = null;

            if (type == null)
            {
                reason = "the type is unknown";
                return false;
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                reason = "a UnityEngine.Object reference cannot be written to a save file";
                return false;
            }

            if (type.IsInterface)
            {
                return true;
            }

            if (type.IsValueType || type == typeof(string))
            {
                reason = $"[SerializeReference] stores classes and interfaces, and '{type.Name}' is neither - " +
                         "drop the attribute and Unity will serialize it normally";
                return false;
            }

            if (IsPlainDictionary(type))
            {
                // True on every version, for different reasons: before 6.6 Unity did not serialize a
                // Dictionary at all, and from 6.6 it does but refuses [SerializeReference] on one.
                reason = NativeDictionaries
                    ? "[SerializeReference] is not allowed on a Dictionary field - drop the attribute"
                    : "Unity does not serialize Dictionary - use SerializableDictionary instead";
                return false;
            }

            return true;
        }

        /// <summary>Whether Unity can persist a value of this type at all.</summary>
        public static bool IsSerializableType(Type type)
        {
            return IsSerializableType(type, out _);
        }

        /// <summary>
        /// Whether Unity can persist a value of this type, and if not, why.
        /// </summary>
        public static bool IsSerializableType(Type type, out string reason)
        {
            return IsSerializableType(type, allowContainer: true, byReference: false, reason: out reason);
        }

        /// <summary>
        /// Whether Unity can persist a value of this type when the field carries
        /// <c>[SerializeReference]</c>.
        /// </summary>
        /// <remarks>
        /// The attribute is not decoration: it switches Unity to storing the value BY REFERENCE, and
        /// the rules genuinely differ. An abstract class or an interface becomes legal - which is the
        /// whole point, since that is the only way to store "some kind of weapon" - and the type no
        /// longer has to carry <c>[Serializable]</c>. What it cannot do is store a value type: there
        /// is no reference to keep.
        /// </remarks>
        public static bool IsSerializableByReference(Type type, out string reason)
        {
            return IsSerializableType(type, allowContainer: true, byReference: true, reason: out reason);
        }

        private static bool IsSerializableType(Type type, bool allowContainer, bool byReference, out string reason)
        {
            reason = null;

            // Containers keep their own rules - a List is still a List - so only the ELEMENT is
            // reached by reference. Everything below this point is the leaf.
            if (byReference && !IsUnityContainer(type))
            {
                return IsByReferenceType(type, out reason);
            }

            if (type == null)
            {
                reason = "the type is unknown";
                return false;
            }

            if (type.IsEnum || type == typeof(string))
            {
                return true;
            }

            if (type.IsPrimitive)
            {
                // Unity has no decimal, and IntPtr is not data.
                if (type == typeof(IntPtr) || type == typeof(UIntPtr))
                {
                    reason = $"Unity does not serialize {type.Name}";
                    return false;
                }

                return true;
            }

            if (type == typeof(decimal))
            {
                reason = "Unity does not serialize decimal - use double or a scaled integer";
                return false;
            }

            if (typeof(UnityEngine.Object).IsAssignableFrom(type))
            {
                reason = "a UnityEngine.Object reference cannot be written to a save file";
                return false;
            }

            if (type.IsArray)
            {
                if (!allowContainer)
                {
                    reason = "Unity does not serialize a collection inside another collection";
                    return false;
                }

                if (type.GetArrayRank() != 1)
                {
                    reason = "Unity only serializes single-dimension arrays";
                    return false;
                }

                return IsSerializableType(type.GetElementType(), allowContainer: false, byReference, out reason);
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                if (!allowContainer)
                {
                    reason = "Unity does not serialize a collection inside another collection";
                    return false;
                }

                return IsSerializableType(type.GetGenericArguments()[0], allowContainer: false, byReference, out reason);
            }

            if (IsPlainDictionary(type))
            {
                if (!NativeDictionaries)
                {
                    reason = "Unity does not serialize Dictionary - use SerializableDictionary instead";
                    return false;
                }

                if (!allowContainer)
                {
                    reason = "Unity does not serialize a Dictionary inside another collection";
                    return false;
                }

                return IsSaveableDictionary(type, out reason);
            }

            if (type.IsInterface)
            {
                reason = "Unity needs a concrete type here";
                return false;
            }

            if (type.IsAbstract)
            {
                reason = "Unity cannot create an abstract type";
                return false;
            }

            if (IsBuiltInStruct(type))
            {
                return true;
            }

            if (Unsupported.TryGetValue(type, out var unsupported))
            {
                reason = unsupported;
                return false;
            }

            if (IsDefined(type, typeof(SerializableAttribute)))
            {
                return true;
            }

            reason = $"'{type.Name}' has no [Serializable]";
            return false;
        }

        /// <summary>Whether this is a plain <c>Dictionary</c>.</summary>
        public static bool IsPlainDictionary(Type type)
        {
            return type != null
                   && type.IsGenericType
                   && type.GetGenericTypeDefinition() == typeof(Dictionary<,>);
        }

        /// <summary>
        /// Whether this editor serializes a plain <c>Dictionary</c> field. Unity 6.6 added it; before
        /// that, <c>SerializableDictionary&lt;K,V&gt;</c> is the only way.
        /// </summary>
        /// <remarks>
        /// This package follows Unity's serializer and nothing else, so the answer has to move with
        /// the editor. Getting it wrong in either direction is the same silent bug: a field the
        /// window shows and the file does not have.
        /// </remarks>
        // static readonly, not const: a const would fold at compile time and turn the other branch
        // into unreachable code, which is a warning in a package that ships with none.
#if UNITY_6000_6_OR_NEWER
        public static readonly bool NativeDictionaries = true;
#else
        public static readonly bool NativeDictionaries = false;
#endif

        /// <summary>
        /// Whether a <c>Dictionary</c> can also reach a save file, and if not, why.
        /// </summary>
        /// <remarks>
        /// Unity 6.6 accepts more key types than a save file can: it will happily serialize a
        /// <c>Dictionary&lt;Vector3, int&gt;</c> into a scene, but JSON has no way to write a vector
        /// as an object key. Refusing it here, with the reason, beats writing a file that cannot be
        /// read back.
        /// </remarks>
        public static bool IsSaveableDictionary(Type type, out string reason)
        {
            reason = null;

            if (!IsPlainDictionary(type))
            {
                reason = $"'{type?.Name}' is not a Dictionary";
                return false;
            }

            var arguments = type.GetGenericArguments();
            var key = arguments[0];

            if (key != typeof(string) && key != typeof(int) && !key.IsEnum)
            {
                reason = $"a save file is JSON, where every key is text, so '{key.Name}' cannot be a " +
                         "dictionary key here - use string, int or an enum";
                return false;
            }

            return IsSerializableType(arguments[1], allowContainer: true, byReference: false, reason: out reason);
        }

        /// <summary>
        /// Whether a save class needs <c>[Serializable]</c>. A class reached through a field does;
        /// the save class itself does not, because the editor holds it as a managed reference.
        /// </summary>
        public static bool NeedsSerializableAttribute(Type type)
        {
            return type != null && !typeof(SaveData).IsAssignableFrom(type);
        }

        private static bool IsDefined(MemberInfo member, Type attributeType)
        {
            return Attribute.IsDefined(member, attributeType, inherit: false);
        }
    }
}
