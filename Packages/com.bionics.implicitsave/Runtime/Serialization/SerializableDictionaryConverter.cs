using System;
using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;
using UnityEngine;

namespace ImplicitSave.Serialization
{
    /// <summary>
    /// Writes a <see cref="SerializableDictionary{TKey,TValue}"/> as a plain JSON object rather than
    /// as the two lists Unity keeps underneath.
    /// </summary>
    /// <remarks>
    /// Without this the file would read <c>{"_keys":["potion"],"_values":[5]}</c>, which is unusable
    /// for anyone opening a save to see what went wrong. With it, the same data reads
    /// <c>{"potion": 5}</c>.
    /// <para>
    /// JSON object keys are always strings, so a non-string key is converted on the way out and
    /// parsed back on the way in - always in the invariant culture, or a save written on a machine
    /// with one decimal separator would be unreadable on a machine with another.
    /// </para>
    /// </remarks>
    public sealed class SerializableDictionaryConverter : JsonConverter
    {
        // The dictionary is only known as SerializableDictionary<,> here, so the work is handed to a
        // small typed adapter. Building one costs reflection, hence the cache: the autosave tick
        // serializes every live save, so this runs far more often than a save file is written.
        private static readonly Dictionary<Type, IDictionaryAdapter> Adapters =
            new Dictionary<Type, IDictionaryAdapter>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // The cache holds Type objects, which do not survive a domain reload intact.
            Adapters.Clear();
        }

        /// <inheritdoc />
        public override bool CanConvert(Type objectType)
        {
            return FindDictionaryType(objectType) != null;
        }

        /// <inheritdoc />
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

            GetAdapter(value.GetType()).Write(writer, value, serializer);
        }

        /// <inheritdoc />
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            if (reader.TokenType != JsonToken.StartObject)
            {
                throw new JsonSerializationException(
                    $"Expected a JSON object for '{objectType.Name}' but found {reader.TokenType}.");
            }

            var result = existingValue ?? Activator.CreateInstance(objectType);
            GetAdapter(objectType).Read(reader, result, serializer);
            return result;
        }

        private static IDictionaryAdapter GetAdapter(Type objectType)
        {
            if (Adapters.TryGetValue(objectType, out var cached))
            {
                return cached;
            }

            var dictionaryType = FindDictionaryType(objectType);
            if (dictionaryType == null)
            {
                throw new JsonSerializationException($"'{objectType.Name}' is not a SerializableDictionary.");
            }

            var arguments = dictionaryType.GetGenericArguments();
            var adapterType = typeof(DictionaryAdapter<,>).MakeGenericType(arguments[0], arguments[1]);
            var adapter = (IDictionaryAdapter)Activator.CreateInstance(adapterType);

            Adapters[objectType] = adapter;
            return adapter;
        }

        /// <summary>
        /// Finds the <c>SerializableDictionary&lt;,&gt;</c> in a type's ancestry, so a user's named
        /// subclass works as well as the generic type itself.
        /// </summary>
        private static Type FindDictionaryType(Type type)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                if (current.IsGenericType && current.GetGenericTypeDefinition() == typeof(SerializableDictionary<,>))
                {
                    return current;
                }
            }

            return null;
        }

        /// <summary>Does the reading and writing once the key and value types are known.</summary>
        private interface IDictionaryAdapter
        {
            void Write(JsonWriter writer, object dictionary, JsonSerializer serializer);
            void Read(JsonReader reader, object dictionary, JsonSerializer serializer);
        }

        private sealed class DictionaryAdapter<TKey, TValue> : IDictionaryAdapter
        {
            public void Write(JsonWriter writer, object dictionary, JsonSerializer serializer)
            {
                var typed = (IDictionary<TKey, TValue>)dictionary;

                writer.WriteStartObject();

                foreach (var pair in typed)
                {
                    writer.WritePropertyName(KeyToString(pair.Key));
                    serializer.Serialize(writer, pair.Value, typeof(TValue));
                }

                writer.WriteEndObject();
            }

            public void Read(JsonReader reader, object dictionary, JsonSerializer serializer)
            {
                var typed = (IDictionary<TKey, TValue>)dictionary;
                typed.Clear();

                while (reader.Read())
                {
                    if (reader.TokenType == JsonToken.EndObject)
                    {
                        return;
                    }

                    if (reader.TokenType != JsonToken.PropertyName)
                    {
                        throw new JsonSerializationException(
                            $"Unexpected {reader.TokenType} while reading a dictionary.");
                    }

                    var rawKey = (string)reader.Value;
                    reader.Read();

                    var value = serializer.Deserialize<TValue>(reader);
                    typed[StringToKey(rawKey)] = value;
                }

                throw new JsonSerializationException("The dictionary object was never closed.");
            }

            /// <summary>Converts a key to the string that becomes a JSON property name.</summary>
            private static string KeyToString(TKey key)
            {
                if (key == null)
                {
                    throw new JsonSerializationException("A dictionary key cannot be null in JSON.");
                }

                if (key is string text)
                {
                    return text;
                }

                if (typeof(TKey).IsEnum)
                {
                    // The name, not the number: renumbering an enum must not silently remap data a
                    // player already has.
                    return key.ToString();
                }

                return Convert.ToString(key, CultureInfo.InvariantCulture);
            }

            /// <summary>Converts a JSON property name back into a key.</summary>
            private static TKey StringToKey(string raw)
            {
                if (typeof(TKey) == typeof(string))
                {
                    return (TKey)(object)raw;
                }

                try
                {
                    if (typeof(TKey).IsEnum)
                    {
                        return (TKey)Enum.Parse(typeof(TKey), raw, ignoreCase: false);
                    }

                    return (TKey)Convert.ChangeType(raw, typeof(TKey), CultureInfo.InvariantCulture);
                }
                catch (Exception e)
                {
                    throw new JsonSerializationException($"'{raw}' is not a valid {typeof(TKey).Name} key.", e);
                }
            }
        }
    }
}
