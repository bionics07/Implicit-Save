using System;
using Newtonsoft.Json;

namespace ImplicitSave
{
    /// <summary>
    /// Base class for every save payload. Deriving from it and declaring fields is all that is
    /// required - ImplicitSave discovers the type, gives it a file and keeps a single live instance
    /// per profile.
    /// </summary>
    /// <remarks>
    /// Save data is restricted to Unity's serializable subset: public fields, or private fields
    /// marked with <c>[SerializeField]</c>. Properties are never persisted. Use
    /// <c>SerializableDictionary&lt;TKey, TValue&gt;</c> instead of <c>Dictionary</c>.
    /// <para>
    /// Instances are owned by ImplicitSave and are only safe to touch from the main thread.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// [SaveId("items")]
    /// public class ItemsSaveData : SaveData
    /// {
    ///     public int SelectedSlot;
    /// }
    /// </code>
    /// </example>
    public abstract class SaveData
    {
        /// <summary>
        /// Schema version of this class. Increment it whenever fields change in a way older files
        /// cannot be read as-is, and ship an <c>ISaveMigration</c> for the step.
        /// </summary>
        /// <remarks>
        /// Stored in the file envelope, never inside the payload itself.
        /// </remarks>
        [JsonIgnore]
        public virtual int SchemaVersion => 1;

        /// <summary>Profile this instance belongs to. Assigned by ImplicitSave on load.</summary>
        [JsonIgnore, NonSerialized] public int ProfileId;

        /// <summary>
        /// Whether this instance is known to differ from what is on disk. Maintained by the dirty
        /// tracker; reading it is safe, writing it directly is not.
        /// </summary>
        [JsonIgnore, NonSerialized] public bool IsDirty;

        /// <summary>
        /// Optional fast-path that flags this instance as changed. Under the default hash-diff
        /// strategy calling it is unnecessary - changes are detected on their own.
        /// </summary>
        public void MarkDirty()
        {
            IsDirty = true;
        }

        /// <summary>
        /// Called on the main thread right before this instance is serialized. Use it to fold
        /// runtime-only state into persisted fields.
        /// </summary>
        protected internal virtual void OnBeforeSave()
        {
        }

        /// <summary>
        /// Called on the main thread right after this instance has been read from storage. Not
        /// called for instances created fresh because no file existed - those get
        /// <see cref="ResetToDefaults"/> instead.
        /// </summary>
        protected internal virtual void OnAfterLoad()
        {
        }

        /// <summary>
        /// Returns this instance to its new-game state. Called when no save file exists yet, when a
        /// file is unrecoverable, and by the editor's Reset button.
        /// </summary>
        public virtual void ResetToDefaults()
        {
        }
    }
}
