using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ImplicitSave.Serialization
{
    /// <summary>
    /// Writes and reads polymorphic values, tagging each one with the registry id of its concrete
    /// type under <c>$t</c>.
    /// </summary>
    /// <remarks>
    /// A save that holds a <c>Weapon</c> has to record which kind of weapon it was, or loading it
    /// back is guesswork. Newtonsoft's own answer, <c>TypeNameHandling</c>, writes an
    /// assembly-qualified .NET name - so renaming the class, moving it to another assembly or
    /// changing its namespace breaks every save that used it. That is the exact pain
    /// <see cref="SaveIdAttribute"/> exists to remove, and it is also a known unsafe-deserialization
    /// vector.
    /// <para>
    /// So the discriminator is the same stable id the rest of the package uses:
    /// <c>[SaveType("melee_weapon")]</c> reaches the file, the .NET type never does.
    /// </para>
    /// <code>
    /// { "$t": "melee_weapon", "Name": "Sword", "Damage": 12, "Reach": 1.5 }
    /// </code>
    /// </remarks>
    public sealed class PolymorphicConverter : JsonConverter
    {
        /// <summary>The field naming the concrete type. Prefixed so it cannot collide with a user field.</summary>
        public const string TypeKey = "$t";

        /// <summary>
        /// The instance the contract resolver hands to every polymorphic property. Stateless apart
        /// from a cache, so one is enough.
        /// </summary>
        internal static readonly PolymorphicConverter Shared = new PolymorphicConverter();

        private JsonSerializer _cachedSource;
        private JsonSerializer _cachedCopy;

        /// <inheritdoc />
        public override bool CanConvert(Type objectType)
        {
            return IsPolymorphic(objectType);
        }

        /// <summary>
        /// Whether values of this type need a discriminator: the declared type cannot be created on
        /// its own, so something has to say which subtype was stored.
        /// </summary>
        internal static bool IsPolymorphic(Type type)
        {
            if (type == null || type == typeof(string) || type.IsPrimitive || type.IsEnum)
            {
                return false;
            }

            return type.IsAbstract || type.IsInterface;
        }

        /// <inheritdoc />
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            var type = value.GetType();

            if (!SaveTypeRegistry.TryGetId(type, out var id))
            {
                throw new SaveSerializationException(
                    $"'{type.FullName}' is stored polymorphically but has no id. Put [SaveType(\"some_id\")] on " +
                    "it - without one there is nothing to write into the file that survives a rename.", null);
            }

            // Serialized without this converter in play, or writing the concrete type would recurse
            // straight back into it.
            var tree = JObject.FromObject(value, WithoutSelf(serializer));
            tree.AddFirst(new JProperty(TypeKey, id));
            tree.WriteTo(writer);
        }

        /// <inheritdoc />
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            var tree = JObject.Load(reader);
            var id = tree[TypeKey]?.Value<string>();

            if (string.IsNullOrEmpty(id))
            {
                throw new SaveSerializationException(
                    $"A '{objectType.Name}' in the file has no '{TypeKey}', so there is no way to tell which " +
                    "subtype it was.", null);
            }

            if (!SaveTypeRegistry.TryGetType(id, out var concrete))
            {
                return Unresolved(id, objectType);
            }

            if (!objectType.IsAssignableFrom(concrete))
            {
                throw new SaveSerializationException(
                    $"'{id}' resolves to '{concrete.FullName}', which is not a '{objectType.Name}'.", null);
            }

            tree.Remove(TypeKey);
            return tree.ToObject(concrete, WithoutSelf(serializer));
        }

        /// <summary>
        /// Handles a <c>$t</c> nothing answers to: the class was deleted, or its id changed without
        /// listing the old one in <see cref="SaveTypeAttribute.PreviousIds"/>.
        /// </summary>
        /// <remarks>
        /// This drops one value and keeps the rest of the save, rather than refusing the file. The
        /// reasoning is about who is holding the file. Removing a class is something a DEVELOPER
        /// does on purpose; the person who then cannot open their save is a PLAYER, who did nothing
        /// and loses everything - inventory, progress, hours - because one weapon type is gone.
        /// Trading the whole save to avoid losing part of it is the worse deal.
        /// <para>
        /// So the value becomes null, which is a state every by-reference field can already be in,
        /// and the loss is reported as an error rather than a warning, because in the editor it is
        /// almost always a mistake worth fixing before shipping. A game that would genuinely rather
        /// fail loudly can set <c>FailOnUnknownSubtype</c>.
        /// </para>
        /// <para>
        /// One thing to be aware of: the next save writes the file WITHOUT the dropped value, making
        /// the loss permanent. The previous contents survive one generation in the <c>.bak</c>, which
        /// is the window you have to notice and put the type back.
        /// </para>
        /// </remarks>
        private static object Unresolved(string id, Type objectType)
        {
            var message =
                $"A '{objectType.Name}' in this save was stored as '{id}', and nothing in this build answers to " +
                "that id. Either the class was deleted, or its [SaveType] id changed - if it was renamed, add " +
                $"PreviousIds = new[] {{ \"{id}\" }} to the [SaveType] on the class it became.";

            if (ImplicitSaveSettings.Instance.FailOnUnknownSubtype)
            {
                throw new SaveSerializationException(message, null);
            }

            ImplicitSaveLog.Error(
                message + " The value was dropped and the rest of the save loaded; the next save writes the file " +
                "without it.");

            return null;
        }

        /// <summary>
        /// A serializer carrying the same settings minus this converter, for the one hop that reads
        /// or writes the concrete type.
        /// </summary>
        private JsonSerializer WithoutSelf(JsonSerializer source)
        {
            // Every polymorphic value in a save would otherwise build one of these. There are only
            // ever one or two distinct callers, so remembering the last is enough.
            if (ReferenceEquals(source, _cachedSource))
            {
                return _cachedCopy;
            }

            var copy = new JsonSerializer
            {
                ContractResolver = source.ContractResolver,
                Culture = source.Culture,
                DateParseHandling = source.DateParseHandling,
                MissingMemberHandling = source.MissingMemberHandling,
                NullValueHandling = source.NullValueHandling,
                ReferenceLoopHandling = source.ReferenceLoopHandling,
                TypeNameHandling = TypeNameHandling.None
            };

            foreach (var converter in source.Converters)
            {
                if (!ReferenceEquals(converter, this))
                {
                    copy.Converters.Add(converter);
                }
            }

            _cachedSource = source;
            _cachedCopy = copy;
            return copy;
        }

        /// <summary>
        /// Finds values reachable more than once from a save, which the file cannot represent.
        /// </summary>
        /// <remarks>
        /// Unity's <c>[SerializeReference]</c> keeps one instance shared across the graph; JSON has
        /// no notion of that, so writing produces two copies and loading gives back two objects.
        /// The editor window would still show one shared value, so the divergence is invisible until
        /// a player reports that editing one weapon stopped changing the other.
        /// </remarks>
        internal static IReadOnlyList<object> FindSharedReferences(object root)
        {
            var seen = new Dictionary<object, int>(ReferenceEqualityComparer.Instance);
            var shared = new List<object>();

            Walk(root, seen, shared, 0);
            return shared;
        }

        private static void Walk(object value, Dictionary<object, int> seen, List<object> shared, int depth)
        {
            if (value == null || depth > UnitySerializationRules.MaxNestingDepth + 2)
            {
                return;
            }

            var type = value.GetType();

            if (type.IsPrimitive || type.IsEnum || type == typeof(string))
            {
                return;
            }

            if (value is System.Collections.IEnumerable enumerable && !(value is string))
            {
                foreach (var item in enumerable)
                {
                    Walk(item, seen, shared, depth + 1);
                }

                return;
            }

            if (seen.TryGetValue(value, out var count))
            {
                if (count == 1)
                {
                    shared.Add(value);
                }

                seen[value] = count + 1;
                return;
            }

            seen[value] = 1;

            foreach (var field in UnitySerializationRules.GetSerializedFields(type))
            {
                Walk(field.GetValue(value), seen, shared, depth + 1);
            }
        }

        /// <summary>Compares by identity, so two equal-but-separate objects are not confused.</summary>
        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            internal static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            public new bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }
    }
}
