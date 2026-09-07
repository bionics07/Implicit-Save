using System;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ImplicitSave.Serialization
{
    /// <summary>
    /// The default serializer: Newtonsoft.Json, writing UTF-8 JSON wrapped in a
    /// <see cref="SaveEnvelope"/>.
    /// </summary>
    /// <remarks>
    /// Newtonsoft rather than <c>JsonUtility</c> because save data needs custom converters -
    /// readable dictionaries and polymorphism both depend on them - and because migration needs to
    /// walk the payload as a tree before any type exists.
    /// <para>
    /// Type names are never written to the file. A subtype is identified by a registry
    /// discriminator, so renaming a class cannot break a player's save.
    /// </para>
    /// </remarks>
    public sealed class NewtonsoftSaveSerializer : ISaveSerializer
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private readonly JsonSerializer _serializer;
        private readonly Formatting _formatting;

        /// <param name="prettyPrint">
        /// Indent the JSON. On by default: a readable save file is worth more during development
        /// than the bytes it costs.
        /// </param>
        public NewtonsoftSaveSerializer(bool prettyPrint = true)
        {
            _formatting = prettyPrint ? Formatting.Indented : Formatting.None;

            _serializer = JsonSerializer.Create(new JsonSerializerSettings
            {
                // Never TypeNameHandling: it writes assembly-qualified names into the file, which
                // turns any refactor into a broken save and is a deserialization risk besides.
                TypeNameHandling = TypeNameHandling.None,
                MissingMemberHandling = MissingMemberHandling.Ignore,
                DateParseHandling = DateParseHandling.None,
                Culture = CultureInfo.InvariantCulture,
                ReferenceLoopHandling = ReferenceLoopHandling.Error
            });
        }

        /// <inheritdoc />
        public byte[] Serialize(SaveData data, string saveId)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            try
            {
                var envelope = new SaveEnvelope
                {
                    SaveId = saveId,
                    SchemaVersion = data.SchemaVersion,
                    SavedAt = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
                    AppVersion = Application.version,
                    Data = JObject.FromObject(data, _serializer)
                };

                var json = SerializeToString(envelope);
                return Utf8.GetBytes(json);
            }
            catch (Exception e) when (!(e is SaveException))
            {
                throw new SaveSerializationException(
                    $"Could not serialize '{data.GetType().Name}'. A field is likely of a type Unity " +
                    "cannot serialize.", e);
            }
        }

        /// <inheritdoc />
        public SaveData Deserialize(byte[] content, Type type)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            try
            {
                var envelope = ReadEnvelope(content);

                if (envelope.Data == null)
                {
                    throw new SaveSerializationException(
                        $"The file has no '{SaveEnvelope.DataKey}' object, so there is nothing to read " +
                        $"into '{type.Name}'.", null);
                }

                var instance = envelope.Data.ToObject(type, _serializer);
                if (instance is SaveData saveData)
                {
                    return saveData;
                }

                throw new SaveSerializationException(
                    $"'{type.Name}' does not derive from {nameof(SaveData)}.", null);
            }
            catch (Exception e) when (!(e is SaveException))
            {
                throw new SaveSerializationException($"Could not read a '{type.Name}' from the file.", e);
            }
        }

        /// <summary>
        /// Reads only the envelope. The migration pipeline uses this to inspect
        /// <see cref="SaveEnvelope.SchemaVersion"/> before deciding how to read the payload.
        /// </summary>
        internal SaveEnvelope ReadEnvelope(byte[] content)
        {
            var json = Utf8.GetString(content);
            var envelope = JsonConvert.DeserializeObject<SaveEnvelope>(json);

            if (envelope == null || envelope.SaveId == null)
            {
                throw new SaveSerializationException(
                    $"The file is not an ImplicitSave file - no '{SaveEnvelope.SaveIdKey}' in it.", null);
            }

            return envelope;
        }

        private string SerializeToString(SaveEnvelope envelope)
        {
            var builder = new StringBuilder(512);
            using (var writer = new StringWriterInvariant(builder))
            using (var jsonWriter = new JsonTextWriter(writer) { Formatting = _formatting })
            {
                _serializer.Serialize(jsonWriter, envelope);
            }

            return builder.ToString();
        }

        /// <summary>
        /// A <c>StringWriter</c> pinned to the invariant culture, so a player's locale cannot change
        /// how numbers land in the file.
        /// </summary>
        private sealed class StringWriterInvariant : System.IO.StringWriter
        {
            internal StringWriterInvariant(StringBuilder builder) : base(builder, CultureInfo.InvariantCulture)
            {
            }
        }
    }
}
