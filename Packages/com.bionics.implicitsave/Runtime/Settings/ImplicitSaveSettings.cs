using UnityEngine;

namespace ImplicitSave
{
    /// <summary>How the package decides that a save is worth writing.</summary>
    public enum DirtyStrategy
    {
        /// <summary>
        /// Compare a hash of the serialized bytes against the last write. Correct by construction:
        /// it notices a change wherever it happened, including inside a collection.
        /// </summary>
        HashDiff,

        /// <summary>
        /// Only write when <see cref="SaveData.MarkDirty"/> was called. Cheaper, and it fails
        /// silently the day someone forgets the call - which is the day a player loses progress.
        /// </summary>
        ManualFlag,

        /// <summary>
        /// Treat <see cref="SaveData.MarkDirty"/> as a fast path and fall back to the hash when the
        /// flag is not set. Same safety as <see cref="HashDiff"/>, less hashing.
        /// </summary>
        Both
    }

    /// <summary>
    /// Project-wide settings. Optional: with no asset present the package runs on the defaults
    /// below, which are the ones most games want anyway.
    /// </summary>
    /// <remarks>
    /// Lives at <c>Assets/Resources/ImplicitSaveSettings.asset</c> when it exists. Create it with
    /// <c>Tools &gt; ImplicitSave &gt; Create Settings Asset</c>.
    /// </remarks>
    public class ImplicitSaveSettings : ScriptableObject
    {
        /// <summary>Name of the asset, and the Resources path it is loaded from.</summary>
        public const string AssetName = "ImplicitSaveSettings";

        [Header("Autosave")]
        [Tooltip("Seconds between autosave ticks. A tick that finds no changes writes nothing.")]
        public float AutoSaveIntervalSeconds = 30f;

        [Tooltip("Turn the periodic tick off entirely. The application hooks below still run.")]
        public bool AutoSaveEnabled = true;

        [Header("Application hooks")]
        [Tooltip("Write when the app is paused. This is the one that matters on mobile.")]
        public bool SaveOnPause = true;

        [Tooltip("Write when the app loses focus. Covers alt-tab on desktop.")]
        public bool SaveOnFocusLost = true;

        [Tooltip("Write on quit. Not reliable on mobile, which is why the other two exist.")]
        public bool SaveOnQuit = true;

        [Header("Behaviour")]
        [Tooltip("How the package decides a save changed. HashDiff is correct by construction.")]
        public DirtyStrategy DirtyStrategy = DirtyStrategy.HashDiff;

        [Tooltip("Indent the JSON. Readable save files are worth more than the bytes they cost.")]
        public bool PrettyPrint = true;

        [Tooltip("Keep the previous version of each file as .bak, for recovery.")]
        public bool KeepBackups = true;

        [Tooltip("Folder under the persistent data path where saves live.")]
        public string SaveFolderName = "saves";

        [Tooltip("Log every load and write. Off by default so the package stays quiet.")]
        public bool VerboseLogging;

        [Tooltip("Fail the whole load when a save holds a subtype this build no longer has. " +
                 "Off by default: the value is dropped and logged, and the rest of the save survives.")]
        public bool FailOnUnknownSubtype;

        private static ImplicitSaveSettings _instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Static state survives play sessions when Disable Domain Reload is on.
            _instance = null;
        }

        /// <summary>
        /// The settings in use. Falls back to defaults when no asset exists, so the package works
        /// in a project that never created one.
        /// </summary>
        public static ImplicitSaveSettings Instance
        {
            get
            {
                if (_instance != null)
                {
                    return _instance;
                }

                _instance = Resources.Load<ImplicitSaveSettings>(AssetName);

                if (_instance == null)
                {
                    _instance = CreateInstance<ImplicitSaveSettings>();
                    _instance.hideFlags = HideFlags.HideAndDontSave;
                }

                ImplicitSaveLog.VerboseLogging = _instance.VerboseLogging;
                return _instance;
            }
        }

        /// <summary>Replaces the settings in use. For tests.</summary>
        public static void Override(ImplicitSaveSettings settings)
        {
            _instance = settings;
            ImplicitSaveLog.VerboseLogging = settings != null && settings.VerboseLogging;
        }
    }
}
