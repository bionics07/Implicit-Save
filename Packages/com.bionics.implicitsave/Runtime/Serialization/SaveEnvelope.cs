using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ImplicitSave.Serialization
{
    /// <summary>
    /// What a save file actually contains: the payload under <c>data</c>, wrapped in the metadata
    /// migration and diagnostics need.
    /// </summary>
    /// <remarks>
    /// The metadata keys are prefixed with <c>$</c> so they can never collide with a field the user
    /// declared. Without this wrapper there is no way to migrate a file or to notice one written by
    /// a newer build.
    /// </remarks>
    internal sealed class SaveEnvelope
    {
        /// <summary>Field name of <see cref="SaveId"/>, used when reading the envelope by hand.</summary>
        internal const string SaveIdKey = "$saveId";

        /// <summary>Field name of <see cref="SchemaVersion"/>.</summary>
        internal const string SchemaVersionKey = "$schemaVersion";

        /// <summary>Field name of the payload.</summary>
        internal const string DataKey = "data";

        [JsonProperty(SaveIdKey, Order = -4)]
        internal string SaveId { get; set; }

        [JsonProperty(SchemaVersionKey, Order = -3)]
        internal int SchemaVersion { get; set; }

        /// <summary>UTC timestamp of the write, ISO 8601. Diagnostics only.</summary>
        [JsonProperty("$savedAt", Order = -2)]
        internal string SavedAt { get; set; }

        /// <summary>Value of <c>Application.version</c> at write time. Diagnostics only.</summary>
        [JsonProperty("$appVersion", Order = -1)]
        internal string AppVersion { get; set; }

        /// <summary>
        /// The user's payload, kept as a tree rather than a typed object so the migration pipeline
        /// can rewrite it before any type is involved.
        /// </summary>
        [JsonProperty(DataKey)]
        internal JObject Data { get; set; }
    }
}
