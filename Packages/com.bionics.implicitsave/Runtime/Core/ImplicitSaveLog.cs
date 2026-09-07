using UnityEngine;

namespace ImplicitSave
{
    /// <summary>
    /// Every log line the package emits goes through here, so all of them carry the same prefix and
    /// obey the same verbosity switch.
    /// </summary>
    internal static class ImplicitSaveLog
    {
        private const string Prefix = "[ImplicitSave] ";

        /// <summary>
        /// Whether informational lines are emitted. Warnings and errors are always emitted.
        /// Mirrors <c>ImplicitSaveSettings.VerboseLogging</c> once settings exist.
        /// </summary>
        internal static bool VerboseLogging;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Static state survives play sessions when Disable Domain Reload is on.
            VerboseLogging = false;
        }

        internal static void Info(string message)
        {
            if (VerboseLogging)
            {
                Debug.Log(Prefix + message);
            }
        }

        internal static void Warning(string message)
        {
            Debug.LogWarning(Prefix + message);
        }

        internal static void Error(string message)
        {
            Debug.LogError(Prefix + message);
        }
    }
}
