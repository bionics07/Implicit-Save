using System;
using Newtonsoft.Json.Linq;

namespace ImplicitSave.Serialization
{
    /// <summary>
    /// Converts a <see cref="SaveData"/> instance to the bytes that go to storage and back.
    /// Implementations own the file envelope, not just the payload.
    /// </summary>
    /// <remarks>
    /// Serialization always runs on the main thread: save instances may hold Unity types and the
    /// game may mutate them from anywhere, so serializing off-thread is a data race.
    /// </remarks>
    public interface ISaveSerializer
    {
        /// <summary>Converts <paramref name="data"/> into the bytes to persist.</summary>
        /// <param name="data">The instance to serialize.</param>
        /// <param name="saveId">Stable id recorded in the envelope.</param>
        /// <exception cref="SaveSerializationException">The instance could not be serialized.</exception>
        byte[] Serialize(SaveData data, string saveId);

        /// <summary>Rebuilds an instance of <paramref name="type"/> from persisted bytes.</summary>
        /// <param name="content">Bytes previously produced by <see cref="Serialize"/>.</param>
        /// <param name="type">Concrete <see cref="SaveData"/> type to produce.</param>
        /// <exception cref="SaveSerializationException">The bytes are not readable as this type.</exception>
        SaveData Deserialize(byte[] content, Type type);

        /// <summary>
        /// Reads the envelope without touching the payload, so the schema version can be checked
        /// before anything is turned into an object.
        /// </summary>
        /// <remarks>
        /// This split is what makes migration and future-version protection possible: an old file
        /// has to be rewritten before a type sees it, and a newer file must never be deserialized at
        /// all.
        /// </remarks>
        /// <exception cref="SaveSerializationException">The bytes are not an ImplicitSave file.</exception>
        SaveEnvelope ReadEnvelope(byte[] content);

        /// <summary>Turns an already-read payload into an instance.</summary>
        /// <param name="payload">The payload, migrated if it needed to be.</param>
        /// <param name="type">Concrete <see cref="SaveData"/> type to produce.</param>
        /// <exception cref="SaveSerializationException">The payload is not readable as this type.</exception>
        SaveData DeserializePayload(JObject payload, Type type);
    }
}
