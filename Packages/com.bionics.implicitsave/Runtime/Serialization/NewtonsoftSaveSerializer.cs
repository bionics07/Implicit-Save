using System;
using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;
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

        private readonly JsonSerializer _payloadSerializer;
        private readonly JsonSerializer _envelopeSerializer;
        private readonly Formatting _formatting;

        /// <param name="prettyPrint">
        /// Indent the JSON. On by default: a readable save file is worth more during development
        /// than the bytes it costs.
        /// </param>
        public NewtonsoftSaveSerializer(bool prettyPrint = true)
        {
            _formatting = prettyPrint ? Formatting.Indented : Formatting.None;

            // The payload follows Unity's rules exactly, so the editor window and the file can never
            // disagree about what a save contains.
            _payloadSerializer = JsonSerializer.Create(CreateSettings(UnityContractResolver.Instance));

            // The envelope is the package's own bookkeeping, not user save data. It is written with
            // Newtonsoft's normal rules because its members are properties, which the Unity rules
            // deliberately exclude.
            _envelopeSerializer = JsonSerializer.Create(CreateSettings(resolver: null));
        }

        private static JsonSerializerSettings CreateSettings(IContractResolver resolver)
        {
            var settings = new JsonSerializerSettings
            {
                // Never TypeNameHandling: it writes assembly-qualified names into the file, which
                // turns any refactor into a broken save and is a deserialization risk besides.
                TypeNameHandling = TypeNameHandling.None,
                MissingMemberHandling = MissingMemberHandling.Ignore,

                // Without this, a string the player saved that merely looks like a date comes back
                // as a reformatted DateTime. Silent data loss, and impossible to explain later.
                DateParseHandling = DateParseHandling.None,
                Culture = CultureInfo.InvariantCulture,
                ReferenceLoopHandling = ReferenceLoopHandling.Error
            };

            settings.Converters.Add(new SerializableDictionaryConverter());

            if (resolver != null)
            {
                settings.ContractResolver = resolver;

                // Only the payload gets this. The resolver puts it on each ambiguous field, which is
                // what makes a $t appear at all; having it in the list as well covers the calls that
                // start from a declared type directly - a dictionary's value type, say - where there
                // is no property to hang it on.
                settings.Converters.Add(PolymorphicConverter.Shared);
            }

            return settings;
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
                    Data = JObject.FromObject(data, _payloadSerializer)
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

                return DeserializePayload(envelope.Data, type);
            }
            catch (Exception e) when (!(e is SaveException))
            {
                throw new SaveSerializationException($"Could not read a '{type.Name}' from the file.", e);
            }
        }

        /// <inheritdoc />
        public SaveData DeserializePayload(JObject payload, Type type)
        {
            if (payload == null)
            {
                throw new ArgumentNullException(nameof(payload));
            }

            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            try
            {
                var instance = payload.ToObject(type, _payloadSerializer);
                if (instance is SaveData saveData)
                {
                    return saveData;
                }

                throw new SaveSerializationException(
                    $"'{type.Name}' does not derive from {nameof(SaveData)}.", null);
            }
            catch (Exception e) when (!(e is SaveException))
            {
                throw new SaveSerializationException($"Could not read a '{type.Name}' from the payload.", e);
            }
        }

        /// <inheritdoc />
        public SaveEnvelope ReadEnvelope(byte[] content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            SaveEnvelope envelope;

            try
            {
                var json = Utf8.GetString(content);

                using (var reader = new JsonTextReader(new System.IO.StringReader(json)))
                {
                    // The project's global JsonConvert defaults must not reach in here - a user who
                    // set their own would silently change how every save file is read.
                    envelope = _envelopeSerializer.Deserialize<SaveEnvelope>(reader);
                }
            }
            catch (Exception e)
            {
                // A truncated file throws a Newtonsoft exception, and letting that out would both
                // leak the dependency and slip past the recovery path, which only catches
                // SaveException.
                throw new SaveSerializationException("The file could not be parsed as JSON.", e);
            }

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
                _envelopeSerializer.Serialize(jsonWriter, envelope);
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
