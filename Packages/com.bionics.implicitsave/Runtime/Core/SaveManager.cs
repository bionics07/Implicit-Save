using System;
using System.Collections.Generic;
using ImplicitSave.Serialization;
using ImplicitSave.Storage;
using UnityEngine;

namespace ImplicitSave
{
    /// <summary>
    /// The API a game uses. Declare a <see cref="SaveData"/> class, then reach for it here - there
    /// is nothing to register, wire up or drag into a field.
    /// </summary>
    /// <remarks>
    /// A thin front for <see cref="SaveRepository"/> and <see cref="ProfileService"/>; it holds no
    /// logic of its own. It builds itself on first use with file storage, so a game can call
    /// <see cref="Get{T}()"/> without any setup at all.
    /// <para>
    /// <b>Main thread only.</b> Save instances may hold Unity types and the game may mutate them
    /// from anywhere, so calling any of this from a job or a task can corrupt a save in ways that
    /// only show up intermittently.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// var items = SaveManager.Get&lt;ItemsSaveData&gt;();
    /// items.SelectedSlot = 2;
    /// SaveManager.Save&lt;ItemsSaveData&gt;();
    /// </code>
    /// </example>
    public static class SaveManager
    {
        private static SaveRepository _repository;
        private static ProfileService _profiles;

        /// <summary>Raised after a save is written, with its type and profile.</summary>
        public static event Action<Type, int> Saved;

        /// <summary>Raised after a save is read from storage, with its type and profile.</summary>
        public static event Action<Type, int> Loaded;

        /// <summary>Raised when a save could not be read or written.</summary>
        public static event Action<SaveException> Failed;

        /// <summary>Raised after the active profile changes, with the previous id and the new one.</summary>
        public static event Action<int, int> ActiveProfileChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Without this, entering play mode with Disable Domain Reload on would carry the
            // previous session's cached saves and event subscribers into the new one.
            _repository = null;
            _profiles = null;
            Saved = null;
            Loaded = null;
            Failed = null;
            ActiveProfileChanged = null;
        }

        /// <summary>
        /// Replaces the default wiring. Call it before anything else, in a test or to point the
        /// package at storage of your own.
        /// </summary>
        /// <param name="serializer">Converts saves to bytes and back.</param>
        /// <param name="storage">Where those bytes live.</param>
        public static void Configure(ISaveSerializer serializer, ISaveStorage storage)
        {
            Bind(new SaveRepository(serializer, storage), new ProfileService(storage));
        }

        /// <summary>Forgets everything, including the wiring. Mainly for tests.</summary>
        public static void Shutdown()
        {
            _repository = null;
            _profiles = null;
        }

        /// <summary>The profile that <see cref="Get{T}()"/> reads from.</summary>
        public static int ActiveProfileId => Profiles.ActiveProfileId;

        /// <summary>
        /// Switches the active profile, writing the current one to storage first so nothing changed
        /// in memory is lost.
        /// </summary>
        /// <remarks>
        /// Saves already loaded stay loaded, for either profile. A live instance is shared, so
        /// <c>Get&lt;T&gt;(1)</c> and <c>Get&lt;T&gt;()</c> after switching to profile 1 are the same
        /// object - discarding one because the active profile moved would be throwing away the
        /// game's own changes. Call <see cref="Reload"/> when a fresh read from storage is what you
        /// actually want.
        /// </remarks>
        /// <exception cref="SaveException">No such profile.</exception>
        public static void SetActiveProfile(int profileId)
        {
            Repository.SaveAll(Profiles.ActiveProfileId);
            Profiles.SetActive(profileId);
        }

        /// <summary>The live save of this type for the active profile, loaded on first use.</summary>
        /// <typeparam name="T">The save type to fetch.</typeparam>
        public static T Get<T>() where T : SaveData, new()
        {
            return Repository.Get<T>(ActiveProfileId);
        }

        /// <summary>The live save of this type for a specific profile.</summary>
        public static T Get<T>(int profileId) where T : SaveData, new()
        {
            return Repository.Get<T>(profileId);
        }

        /// <summary>Type-erased form of <see cref="Get{T}(int)"/>, for the editor window.</summary>
        public static SaveData Get(Type type, int profileId)
        {
            return Repository.Get(type, profileId);
        }

        /// <summary>Writes one save of the active profile.</summary>
        public static void Save<T>() where T : SaveData
        {
            Repository.Save(typeof(T), ActiveProfileId);
        }

        /// <summary>Writes one save of a specific profile.</summary>
        public static void Save<T>(int profileId) where T : SaveData
        {
            Repository.Save(typeof(T), profileId);
        }

        /// <summary>
        /// Type-erased form of <see cref="Save{T}(int)"/>, for callers that only have a
        /// <see cref="Type"/> - the editor window, and anything driven by the registry.
        /// </summary>
        public static void Save(Type type, int profileId)
        {
            Repository.Save(type, profileId);
        }

        /// <summary>Writes every loaded save of the active profile.</summary>
        public static void SaveAll()
        {
            Repository.SaveAll(ActiveProfileId);
        }

        /// <summary>Drops a profile's saves from memory, discarding anything not written.</summary>
        public static void Reload(int profileId)
        {
            Repository.Reload(profileId);
        }

        /// <summary>Every save slot, for a "load game" screen.</summary>
        public static IReadOnlyList<ProfileInfo> GetProfiles()
        {
            return Profiles.GetProfiles();
        }

        /// <summary>Creates a slot with the lowest free id.</summary>
        public static ProfileInfo CreateProfile(string displayName = null)
        {
            return Profiles.Create(displayName);
        }

        /// <summary>Deletes a slot and every save in it. Not recoverable.</summary>
        public static void DeleteProfile(int profileId)
        {
            Repository.Reload(profileId);
            Profiles.Delete(profileId);
        }

        /// <summary>Copies every save of one slot over another, replacing it.</summary>
        public static void CopyProfile(int source, int destination)
        {
            Repository.SaveAll(source);
            Profiles.Copy(source, destination);
            Repository.Reload(destination);
        }

        /// <summary>Whether a slot exists.</summary>
        public static bool ProfileExists(int profileId)
        {
            return Profiles.Exists(profileId);
        }

        private static SaveRepository Repository
        {
            get
            {
                EnsureBuilt();
                return _repository;
            }
        }

        private static ProfileService Profiles
        {
            get
            {
                EnsureBuilt();
                return _profiles;
            }
        }

        /// <summary>
        /// Builds the default wiring on first use, so a game that never calls
        /// <see cref="Configure"/> still works.
        /// </summary>
        private static void EnsureBuilt()
        {
            if (_repository != null)
            {
                return;
            }

            var storage = new FileSaveStorage();
            Bind(new SaveRepository(new NewtonsoftSaveSerializer(), storage), new ProfileService(storage));
        }

        private static void Bind(SaveRepository repository, ProfileService profiles)
        {
            _repository = repository;
            _profiles = profiles;

            _repository.Failed += OnFailed;
            _repository.Saved += OnSaved;
            _repository.Loaded += OnLoaded;
            _profiles.ActiveProfileChanged += OnActiveProfileChanged;
        }

        private static void OnFailed(SaveException exception)
        {
            Failed?.Invoke(exception);
        }

        private static void OnSaved(Type type, int profileId)
        {
            Saved?.Invoke(type, profileId);
        }

        private static void OnLoaded(Type type, int profileId)
        {
            Loaded?.Invoke(type, profileId);
        }

        private static void OnActiveProfileChanged(int previous, int current)
        {
            ActiveProfileChanged?.Invoke(previous, current);
        }
    }
}
