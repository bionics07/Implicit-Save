using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ImplicitSave.Serialization
{
    /// <summary>
    /// Writes the Unity structs whose stored state is private, so the file holds what the editor
    /// shows instead of an empty object.
    /// </summary>
    /// <remarks>
    /// Half of Unity's built-in structs keep their values in public fields - <c>Vector3.x</c> is a
    /// field, and the ordinary rules already reach it. The other half do not:
    /// <c>Rect</c> stores <c>m_XMin</c>, <c>Bounds</c> stores <c>m_Center</c> and <c>m_Extents</c>,
    /// <c>Vector2Int</c> stores <c>m_X</c>. Those are private with no <c>[SerializeField]</c>, which
    /// this package deliberately skips - so without this converter they serialize as <c>{}</c> and
    /// the value is gone.
    /// <para>
    /// The names written here are the ones from the C# API - <c>center</c>, <c>extents</c>,
    /// <c>width</c> - not Unity's internal <c>m_</c> spellings. A save file is meant to be read and
    /// migrated by hand, and the field a developer knows is the one they will look for.
    /// </para>
    /// </remarks>
    public sealed class UnityStructConverter : JsonConverter
    {
        /// <inheritdoc />
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Vector2Int)
                   || objectType == typeof(Vector3Int)
                   || objectType == typeof(Rect)
                   || objectType == typeof(RectInt)
                   || objectType == typeof(Bounds)
                   || objectType == typeof(BoundsInt)
                   || objectType == typeof(LayerMask)
                   || objectType == typeof(Hash128);
        }

        /// <inheritdoc />
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            switch (value)
            {
                case Vector2Int v:
                    WriteObject(writer, ("x", v.x), ("y", v.y));
                    return;

                case Vector3Int v:
                    WriteObject(writer, ("x", v.x), ("y", v.y), ("z", v.z));
                    return;

                case Rect r:
                    WriteObject(writer, ("x", r.x), ("y", r.y), ("width", r.width), ("height", r.height));
                    return;

                case RectInt r:
                    WriteObject(writer, ("x", r.x), ("y", r.y), ("width", r.width), ("height", r.height));
                    return;

                case Bounds b:
                    writer.WriteStartObject();
                    writer.WritePropertyName("center");
                    serializer.Serialize(writer, b.center, typeof(Vector3));
                    writer.WritePropertyName("extents");
                    serializer.Serialize(writer, b.extents, typeof(Vector3));
                    writer.WriteEndObject();
                    return;

                case BoundsInt b:
                    writer.WriteStartObject();
                    writer.WritePropertyName("position");
                    serializer.Serialize(writer, b.position, typeof(Vector3Int));
                    writer.WritePropertyName("size");
                    serializer.Serialize(writer, b.size, typeof(Vector3Int));
                    writer.WriteEndObject();
                    return;

                case LayerMask mask:
                    // A mask is one integer, and writing it as an object would only obscure that.
                    writer.WriteValue(mask.value);
                    return;

                case Hash128 hash:
                    // Every field of this one is private, so the ordinary rules would write {} and
                    // lose it. Its own text form is exact and readable, which beats inventing one.
                    writer.WriteValue(hash.ToString());
                    return;

                default:
                    throw new SaveSerializationException(
                        $"'{value?.GetType().Name}' is not a Unity struct this converter handles.", null);
            }
        }

        /// <inheritdoc />
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (objectType == typeof(LayerMask))
            {
                var token = JToken.Load(reader);
                return (LayerMask)(token.Type == JTokenType.Null ? 0 : token.Value<int>());
            }

            if (objectType == typeof(Hash128))
            {
                var token = JToken.Load(reader);
                var text = token.Type == JTokenType.Null ? null : token.Value<string>();

                return string.IsNullOrEmpty(text) ? default(Hash128) : Hash128.Parse(text);
            }

            var tree = JToken.Load(reader) as JObject;

            if (tree == null)
            {
                // A null or a scalar where an object was expected. The default is a real value for
                // every one of these types, so it beats failing the whole load.
                return Activator.CreateInstance(objectType);
            }

            if (objectType == typeof(Vector2Int))
            {
                return new Vector2Int(Int(tree, "x"), Int(tree, "y"));
            }

            if (objectType == typeof(Vector3Int))
            {
                return new Vector3Int(Int(tree, "x"), Int(tree, "y"), Int(tree, "z"));
            }

            if (objectType == typeof(Rect))
            {
                return new Rect(Float(tree, "x"), Float(tree, "y"), Float(tree, "width"), Float(tree, "height"));
            }

            if (objectType == typeof(RectInt))
            {
                return new RectInt(Int(tree, "x"), Int(tree, "y"), Int(tree, "width"), Int(tree, "height"));
            }

            if (objectType == typeof(Bounds))
            {
                return new Bounds { center = Vector3Of(tree, "center"), extents = Vector3Of(tree, "extents") };
            }

            if (objectType == typeof(BoundsInt))
            {
                return new BoundsInt(Vector3IntOf(tree, "position"), Vector3IntOf(tree, "size"));
            }

            throw new SaveSerializationException(
                $"'{objectType.Name}' is not a Unity struct this converter handles.", null);
        }

        private static void WriteObject(JsonWriter writer, params (string Name, float Value)[] members)
        {
            writer.WriteStartObject();

            foreach (var member in members)
            {
                writer.WritePropertyName(member.Name);
                writer.WriteValue(member.Value);
            }

            writer.WriteEndObject();
        }

        private static void WriteObject(JsonWriter writer, params (string Name, int Value)[] members)
        {
            writer.WriteStartObject();

            foreach (var member in members)
            {
                writer.WritePropertyName(member.Name);
                writer.WriteValue(member.Value);
            }

            writer.WriteEndObject();
        }

        // A member the file does not carry keeps the type's own zero. A save written before a field
        // existed is the ordinary case, not an error.
        private static float Float(JObject tree, string name)
        {
            var token = tree[name];
            return token == null || token.Type == JTokenType.Null ? 0f : token.Value<float>();
        }

        private static int Int(JObject tree, string name)
        {
            var token = tree[name];
            return token == null || token.Type == JTokenType.Null ? 0 : token.Value<int>();
        }

        private static Vector3 Vector3Of(JObject tree, string name)
        {
            var token = tree[name] as JObject;

            return token == null
                ? Vector3.zero
                : new Vector3(Float(token, "x"), Float(token, "y"), Float(token, "z"));
        }

        private static Vector3Int Vector3IntOf(JObject tree, string name)
        {
            var token = tree[name] as JObject;

            return token == null
                ? Vector3Int.zero
                : new Vector3Int(Int(token, "x"), Int(token, "y"), Int(token, "z"));
        }
    }
}
