using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Serialization;

namespace ImplicitSave.Serialization
{
    /// <summary>
    /// Makes Newtonsoft see exactly the members Unity sees, and nothing else.
    /// </summary>
    /// <remarks>
    /// This is the piece that keeps the editor window and the save file telling the same story. Left
    /// to itself Newtonsoft would serialize properties and skip private fields with
    /// <c>[SerializeField]</c> - the exact opposite of Unity on both counts. A save system where the
    /// window shows one state and the disk holds another is the worst failure mode there is, so the
    /// rules come from <see cref="UnitySerializationRules"/> and nowhere else.
    /// <para>
    /// The parity test in the editor test suite asserts the two lists match. It must never be
    /// removed or relaxed.
    /// </para>
    /// </remarks>
    public sealed class UnityContractResolver : DefaultContractResolver
    {
        /// <summary>A shared instance - the resolver is stateless apart from Newtonsoft's own cache.</summary>
        public static readonly UnityContractResolver Instance = new UnityContractResolver();

        /// <inheritdoc />
        protected override List<MemberInfo> GetSerializableMembers(Type objectType)
        {
            var members = new List<MemberInfo>();

            // Fields only, by Unity's rules. Properties are excluded here rather than filtered
            // later, so there is no path by which one reaches the file.
            foreach (var field in UnitySerializationRules.GetSerializedFields(objectType))
            {
                members.Add(field);
            }

            return members;
        }

        /// <inheritdoc />
        protected override JsonProperty CreateProperty(MemberInfo member, Newtonsoft.Json.MemberSerialization memberSerialization)
        {
            var property = base.CreateProperty(member, memberSerialization);

            // A private field with [SerializeField] is persisted, so Newtonsoft has to be told it
            // may write to it - by default it will not touch a non-public member.
            if (member is FieldInfo field && !field.IsPublic)
            {
                property.Readable = true;
                property.Writable = true;
            }

            AttachPolymorphicConverter(property, member);

            return property;
        }

        /// <summary>
        /// Puts the <c>$t</c> converter on fields whose declared type cannot say what it is holding.
        /// </summary>
        /// <remarks>
        /// This has to happen here, on the property, rather than as a plain converter in the
        /// serializer's list. When Newtonsoft writes a value it picks the converter by the value's
        /// CONCRETE type, so a <c>Weapon</c> field holding a <c>MeleeWeapon</c> would be matched
        /// against <c>MeleeWeapon</c> - which is a perfectly ordinary serializable class, so no
        /// converter would fire and no discriminator would be written. The file would then load back
        /// as nothing at all.
        /// <para>
        /// The resolver is the one place that still knows the DECLARED type, which is exactly the
        /// information the reader will have and the writer must therefore preserve.
        /// </para>
        /// </remarks>
        private static void AttachPolymorphicConverter(JsonProperty property, MemberInfo member)
        {
            var type = property.PropertyType;

            // [SerializeReference] is the trigger, not abstractness. It is the declaration that says
            // this field may hold a subtype, and Unity keeps the subtype for any such field - even
            // one declared as a concrete class. Matching that exactly is the point: the window and
            // the file must not disagree about what is stored.
            if (type == null || !Attribute.IsDefined(member, typeof(UnityEngine.SerializeReference)))
            {
                return;
            }

            // A List<Weapon> or Weapon[] is an ordinary list of ambiguous items, so the tag belongs
            // on the items and not on the list.
            var element = GetElementType(type);

            if (element != null)
            {
                property.ItemConverter = PolymorphicConverter.Shared;
                return;
            }

            property.Converter = PolymorphicConverter.Shared;
        }

        /// <summary>The item type of an array or <c>List&lt;T&gt;</c>, or null if it is neither.</summary>
        private static Type GetElementType(Type type)
        {
            if (type.IsArray)
            {
                return type.GetElementType();
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                return type.GetGenericArguments()[0];
            }

            return null;
        }

        /// <inheritdoc />
        protected override string ResolvePropertyName(string propertyName)
        {
            // Field names go to the file verbatim. Renaming them here would mean the JSON no longer
            // matches what the user declared, and every migration would have to know the mapping.
            return propertyName;
        }
    }
}
