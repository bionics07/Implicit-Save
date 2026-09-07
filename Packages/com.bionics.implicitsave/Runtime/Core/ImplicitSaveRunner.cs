using System;
using UnityEngine;

namespace ImplicitSave
{
    /// <summary>
    /// The hidden object that ticks autosave and listens for the application going away.
    /// </summary>
    /// <remarks>
    /// A game never creates or references this. It puts itself in the scene before the first one
    /// loads, hides itself from the hierarchy, and survives scene changes.
    /// <para>
    /// It writes on three separate hooks rather than one. <c>OnApplicationQuit</c> alone is not
    /// enough: on mobile the OS can kill a backgrounded app without ever calling it, so pause and
    /// focus loss are the ones that actually save a player's progress.
    /// </para>
    /// </remarks>
    [AddComponentMenu("")]
    internal class ImplicitSaveRunner : MonoBehaviour
    {
        /// <summary>How long a synchronous flush waits for the write queue before giving up.</summary>
        private static readonly TimeSpan FlushTimeout = TimeSpan.FromSeconds(5);

        private static ImplicitSaveRunner _instance;
        private static bool _bootstrapped;

        private float _nextTickTime;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // With Disable Domain Reload on, the previous play session's statics survive. Without
            // this the second play would think a runner already exists - and the object from the
            // first session is gone, so nothing would tick at all.
            _instance = null;
            _bootstrapped = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (_bootstrapped || _instance != null)
            {
                return;
            }

            _bootstrapped = true;

            // The scene is the authority, not the static field. In the editor an object flagged
            // DontSave is not destroyed when play mode ends, so a runner from the previous session
            // can still be around while the statics have been reset - and creating another one here
            // would leave two, doubling every write. This is the duplicate the plan warns about.
            if (AdoptExistingRunner())
            {
                return;
            }

            var host = new GameObject("[ImplicitSave]");
            UnityEngine.Object.DontDestroyOnLoad(host);
            host.hideFlags = HideFlags.HideAndDontSave;
            _instance = host.AddComponent<ImplicitSaveRunner>();
        }

        /// <summary>
        /// Takes over a runner left behind by a previous play session, and destroys any extras.
        /// </summary>
        /// <returns><c>true</c> when one was adopted and no new runner is needed.</returns>
        private static bool AdoptExistingRunner()
        {
            // FindObjectsOfTypeAll rather than FindObjectsOfType: the runner is hidden and flagged
            // DontSave, and the ordinary search does not return those.
            var existing = Resources.FindObjectsOfTypeAll<ImplicitSaveRunner>();
            if (existing.Length == 0)
            {
                return false;
            }

            _instance = existing[0];

            for (var i = 1; i < existing.Length; i++)
            {
                ImplicitSaveLog.Info("Removing a duplicate ImplicitSave runner left by a previous session.");
                DestroyImmediate(existing[i].gameObject);
            }

            return true;
        }

        private void Awake()
        {
            // Belt and braces against ending up with two, which would double every write.
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            _nextTickTime = Time.unscaledTime + ImplicitSaveSettings.Instance.AutoSaveIntervalSeconds;
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        private void Update()
        {
            var settings = ImplicitSaveSettings.Instance;

            if (!settings.AutoSaveEnabled || settings.AutoSaveIntervalSeconds <= 0f)
            {
                return;
            }

            // Unscaled: a paused game with Time.timeScale at zero still needs to autosave.
            if (Time.unscaledTime < _nextTickTime)
            {
                return;
            }

            _nextTickTime = Time.unscaledTime + settings.AutoSaveIntervalSeconds;
            Tick();
        }

        /// <summary>
        /// The autosave tick: serialize what is loaded, write only what changed, off the main
        /// thread.
        /// </summary>
        private static void Tick()
        {
            SaveManager.AutoSave();
        }

        private void OnApplicationPause(bool paused)
        {
            if (paused && ImplicitSaveSettings.Instance.SaveOnPause)
            {
                // The most important hook on mobile: after this returns the OS may never give the
                // app another frame.
                FlushNow("pause");
            }
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused && ImplicitSaveSettings.Instance.SaveOnFocusLost)
            {
                FlushNow("focus lost");
            }
        }

        private void OnApplicationQuit()
        {
            if (ImplicitSaveSettings.Instance.SaveOnQuit)
            {
                FlushNow("quit");
            }
        }

        /// <summary>
        /// Writes everything and blocks until it is on disk.
        /// </summary>
        /// <remarks>
        /// Blocking rather than awaiting is deliberate. There may be no later frame to resume on,
        /// and an await that never resumes is the save that never happened.
        /// </remarks>
        private static void FlushNow(string reason)
        {
            ImplicitSaveLog.Info("Flushing saves on " + reason + ".");
            SaveManager.FlushSynchronously(FlushTimeout);
        }
    }
}
