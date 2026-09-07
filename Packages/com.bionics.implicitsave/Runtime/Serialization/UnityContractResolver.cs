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

            return property;
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
