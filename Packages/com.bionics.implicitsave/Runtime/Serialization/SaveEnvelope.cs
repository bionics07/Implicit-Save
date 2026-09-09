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
    /// a newer build - and noticing is what keeps a newer save from being overwritten.
    /// </remarks>
    public sealed class SaveEnvelope
    {
        /// <summary>Field name of <see cref="SaveId"/>, used when reading the envelope by hand.</summary>
        public const string SaveIdKey = "$saveId";

        /// <summary>Field name of <see cref="SchemaVersion"/>.</summary>
        public const string SchemaVersionKey = "$schemaVersion";

        /// <summary>Field name of the payload.</summary>
        public const string DataKey = "data";

        /// <summary>
        /// The one field in the envelope that changes on its own. Named here because the dirty
        /// check has to leave it out - see <c>DirtyTracker.ComputeContentHash</c>.
        /// </summary>
        public const string SavedAtKey = "$savedAt";

        /// <summary>Stable identity of the save this file holds.</summary>
        [JsonProperty(SaveIdKey, Order = -4)]
        public string SaveId { get; set; }

        /// <summary>Schema version the payload was written at.</summary>
        [JsonProperty(SchemaVersionKey, Order = -3)]
        public int SchemaVersion { get; set; }

        /// <summary>UTC timestamp of the write, ISO 8601. Diagnostics only.</summary>
        [JsonProperty("$savedAt", Order = -2)]
        public string SavedAt { get; set; }

        /// <summary>Value of <c>Application.version</c> at write time. Diagnostics only.</summary>
        [JsonProperty("$appVersion", Order = -1)]
        public string AppVersion { get; set; }

        /// <summary>
        /// The user's payload, kept as a tree rather than a typed object so the migration pipeline
        /// can rewrite it before any type is involved.
        /// </summary>
        [JsonProperty(DataKey)]
        public JObject Data { get; set; }
    }
}
