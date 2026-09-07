using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ImplicitSave.Storage;
using Newtonsoft.Json;

namespace ImplicitSave
{
    /// <summary>
    /// Owns the list of save slots and which one is active: creating, deleting, copying and the
    /// <c>profiles.json</c> index that describes them.
    /// </summary>
    /// <remarks>
    /// The index is a convenience, not the truth. The truth is which profile folders exist on disk,
    /// which is why a lost or unreadable index is rebuilt from them instead of ending the session.
    /// <para>
    /// Main thread only, like the rest of the public API.
    /// </para>
    /// </remarks>
    public sealed class ProfileService
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private readonly ISaveStorage _storage;
        private readonly JsonSerializerSettings _settings;
        private readonly List<ProfileInfo> _profiles = new List<ProfileInfo>();

        private int _activeProfileId;
        private bool _loaded;

        /// <summary>
        /// Raised after the active profile changes, with the previous id and the new one.
        /// </summary>
        public event Action<int, int> ActiveProfileChanged;

        /// <param name="storage">Where the index and the profile folders live.</param>
        /// <param name="prettyPrint">Indent the index file.</param>
        public ProfileService(ISaveStorage storage, bool prettyPrint = true)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _settings = new JsonSerializerSettings
            {
                Formatting = prettyPrint ? Formatting.Indented : Formatting.None,
                Culture = CultureInfo.InvariantCulture,
                DateParseHandling = DateParseHandling.None,
                TypeNameHandling = TypeNameHandling.None
            };
            _settings.Converters.Add(new Serialization.SerializableDictionaryConverter());
        }

        /// <summary>The profile reads and writes go to unless one is named explicitly.</summary>
        public int ActiveProfileId
        {
            get
            {
                EnsureLoaded();
                return _activeProfileId;
            }
        }

        /// <summary>Every profile that exists, ordered by id.</summary>
        public IReadOnlyList<ProfileInfo> GetProfiles()
        {
            EnsureLoaded();
            return _profiles;
        }

        /// <summary>Whether a profile with this id exists.</summary>
        public bool Exists(int profileId)
        {
            return Find(profileId) != null;
        }

        /// <summary>The profile with this id, or <c>null</c>.</summary>
        public ProfileInfo Get(int profileId)
        {
            return Find(profileId);
        }

        /// <summary>
        /// Makes a profile active. The caller is responsible for flushing the previous one first -
        /// <c>SaveManager.SetActiveProfile</c> does that.
        /// </summary>
        /// <exception cref="SaveException">No such profile.</exception>
        public void SetActive(int profileId)
        {
            EnsureLoaded();

            if (_activeProfileId == profileId)
            {
                return;
            }

            var profile = Find(profileId);
            if (profile == null)
            {
                throw new SaveException($"Profile {profileId} does not exist, so it cannot be made active.");
            }

            var previous = _activeProfileId;
            _activeProfileId = profileId;
            profile.LastPlayedAt = SaveTimestamp.UtcNow();
            WriteIndex();

            ActiveProfileChanged?.Invoke(previous, profileId);
        }

        /// <summary>
        /// Creates a profile with the lowest free id.
        /// </summary>
        /// <param name="displayName">What to show the player. Defaults to "Slot N".</param>
        public ProfileInfo Create(string displayName = null)
        {
            EnsureLoaded();
            return Create(NextFreeId(), displayName);
        }

        /// <summary>Creates a profile with a specific id.</summary>
        /// <exception cref="SaveException">That id is already taken.</exception>
        public ProfileInfo Create(int profileId, string displayName = null)
        {
            EnsureLoaded();

            if (Find(profileId) != null)
            {
                throw new SaveException($"Profile {profileId} already exists.");
            }

            var profile = new ProfileInfo(profileId, displayName ?? DefaultName(profileId));
            _profiles.Add(profile);
            Sort();
            WriteIndex();

            ImplicitSaveLog.Info($"Created profile {profileId} ('{profile.DisplayName}').");
            return profile;
        }

        /// <summary>
        /// Deletes a profile and every save in it. This is not recoverable, so a game should confirm
        /// with the player first.
        /// </summary>
        public void Delete(int profileId)
        {
            EnsureLoaded();

            var profile = Find(profileId);
            if (profile == null)
            {
                return;
            }

            _storage.DeleteProfile(profileId);
            _profiles.Remove(profile);

            // The active profile cannot be one that no longer exists.
            if (_activeProfileId == profileId)
            {
                _activeProfileId = _profiles.Count > 0 ? _profiles[0].Id : 0;
            }

            WriteIndex();
            ImplicitSaveLog.Info($"Deleted profile {profileId}.");
        }

        /// <summary>
        /// Copies every save from one profile to another, replacing the destination. Used for "copy
        /// slot" and for taking a backup before a risky change.
        /// </summary>
        /// <exception cref="SaveException">The source does not exist.</exception>
        public void Copy(int sourceProfileId, int destinationProfileId)
        {
            EnsureLoaded();

            var source = Find(sourceProfileId);
            if (source == null)
            {
                throw new SaveException($"Profile {sourceProfileId} does not exist, so it cannot be copied.");
            }

            if (sourceProfileId == destinationProfileId)
            {
                return;
            }

            _storage.CopyProfile(sourceProfileId, destinationProfileId);

            var destination = Find(destinationProfileId);
            if (destination == null)
            {
                destination = new ProfileInfo(destinationProfileId, DefaultName(destinationProfileId));
                _profiles.Add(destination);
                Sort();
            }

            // The copy carries the play time and custom fields; the name stays the slot's own, so a
            // player does not end up with two slots called the same thing.
            destination.PlayTimeSeconds = source.PlayTimeSeconds;
            destination.Custom = new SerializableDictionary<string, string>(source.Custom);
            destination.LastPlayedAt = SaveTimestamp.UtcNow();

            WriteIndex();
            ImplicitSaveLog.Info($"Copied profile {sourceProfileId} to {destinationProfileId}.");
        }

        /// <summary>Adds to a profile's play time and writes the index.</summary>
        public void AddPlayTime(int profileId, double seconds)
        {
            var profile = Find(profileId);
            if (profile == null || seconds <= 0)
            {
                return;
            }

            profile.PlayTimeSeconds += seconds;
            WriteIndex();
        }

        /// <summary>Sets one of a profile's custom display fields and writes the index.</summary>
        public void SetCustom(int profileId, string key, string value)
        {
            var profile = Find(profileId);
            if (profile == null)
            {
                return;
            }

            profile.Custom[key] = value;
            profile.LastPlayedAt = SaveTimestamp.UtcNow();
            WriteIndex();
        }

        /// <summary>Drops the in-memory index so the next call reads it from storage again.</summary>
        public void Reload()
        {
            _loaded = false;
            _profiles.Clear();
        }

        private ProfileInfo Find(int profileId)
        {
            EnsureLoaded();

            foreach (var profile in _profiles)
            {
                if (profile.Id == profileId)
                {
                    return profile;
                }
            }

            return null;
        }

        private void EnsureLoaded()
        {
            if (_loaded)
            {
                return;
            }

            // Set before loading: a failure part way through must not leave the service trying to
            // load again from inside its own load.
            _loaded = true;
            _profiles.Clear();
            _activeProfileId = 0;

            if (!_storage.IndexExists())
            {
                RebuildFromStorage("no index exists yet");
                return;
            }

            ProfileIndex index;
            try
            {
                var json = Utf8.GetString(_storage.ReadIndex());
                index = JsonConvert.DeserializeObject<ProfileIndex>(json, _settings);
            }
            catch (Exception e)
            {
                var quarantined = _storage.QuarantineIndex();
                ImplicitSaveLog.Warning(
                    $"The profile index could not be read ({e.Message}). It was kept at '{quarantined}' and " +
                    "the list was rebuilt from the profile folders on disk.");
                RebuildFromStorage("the index was unreadable");
                return;
            }

            if (index?.Profiles == null)
            {
                RebuildFromStorage("the index was empty");
                return;
            }

            _profiles.AddRange(index.Profiles);
            _activeProfileId = index.ActiveProfile;

            // Folders that exist without an entry are real saves the index forgot about. Losing
            // them because a file went stale would be losing the player's game.
            AdoptOrphanFolders();
            Sort();

            if (Find(_activeProfileId) == null)
            {
                _activeProfileId = _profiles.Count > 0 ? _profiles[0].Id : 0;
            }

            if (_profiles.Count == 0)
            {
                CreateDefaultProfile();
            }
        }

        /// <summary>
        /// Builds the profile list from the folders that physically exist. This is the reason a lost
        /// index is an inconvenience rather than a lost game.
        /// </summary>
        private void RebuildFromStorage(string why)
        {
            foreach (var id in _storage.ListProfileIds())
            {
                _profiles.Add(new ProfileInfo(id, DefaultName(id)));
            }

            Sort();

            if (_profiles.Count == 0)
            {
                CreateDefaultProfile();
                return;
            }

            _activeProfileId = _profiles[0].Id;
            ImplicitSaveLog.Info($"Rebuilt {_profiles.Count} profile(s) from disk because {why}.");
            WriteIndex();
        }

        private void AdoptOrphanFolders()
        {
            foreach (var id in _storage.ListProfileIds())
            {
                var known = false;

                foreach (var profile in _profiles)
                {
                    if (profile.Id == id)
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    ImplicitSaveLog.Warning(
                        $"Profile {id} has saves on disk but no entry in the index. It was added back.");
                    _profiles.Add(new ProfileInfo(id, DefaultName(id)));
                }
            }
        }

        private void CreateDefaultProfile()
        {
            // A game always has somewhere to save, even on a first run.
            _profiles.Add(new ProfileInfo(0, DefaultName(0)));
            _activeProfileId = 0;
            WriteIndex();
        }

        private int NextFreeId()
        {
            var id = 0;

            while (Find(id) != null)
            {
                id++;
            }

            return id;
        }

        private void Sort()
        {
            _profiles.Sort((a, b) => a.Id.CompareTo(b.Id));
        }

        private void WriteIndex()
        {
            var index = new ProfileIndex
            {
                ActiveProfile = _activeProfileId,
                Profiles = _profiles
            };

            try
            {
                var json = JsonConvert.SerializeObject(index, _settings);
                _storage.WriteIndex(Utf8.GetBytes(json));
            }
            catch (SaveException e)
            {
                // The index is rebuildable from the folders, so failing to write it must not take
                // down whatever the game was doing.
                ImplicitSaveLog.Error($"Could not write the profile index: {e.Message}");
            }
        }

        private static string DefaultName(int profileId)
        {
            return "Slot " + (profileId + 1).ToString(CultureInfo.InvariantCulture);
        }
    }
}
