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
                reason = "Unity does not serialize Dictionary - use SerializableDictionary instead";
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
                reason = "Unity does not serialize Dictionary - use SerializableDictionary instead";
                return false;
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

            if (IsDefined(type, typeof(SerializableAttribute)))
            {
                return true;
            }

            reason = $"'{type.Name}' has no [Serializable]";
            return false;
        }

        /// <summary>Whether this is a plain <c>Dictionary</c>, the one hole this package fills.</summary>
        public static bool IsPlainDictionary(Type type)
        {
            return type != null
                   && type.IsGenericType
                   && type.GetGenericTypeDefinition() == typeof(Dictionary<,>);
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
