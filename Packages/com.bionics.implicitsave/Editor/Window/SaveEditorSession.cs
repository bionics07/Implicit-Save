using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ImplicitSave.Serialization;
using Newtonsoft.Json;
using ImplicitSave.Storage;
using UnityEditor;
using UnityEngine;

namespace ImplicitSave.Editor
{
    /// <summary>How a save in the list stands relative to its file.</summary>
    public enum SaveFileState
    {
        /// <summary>A file exists and matches the current schema version.</summary>
        UpToDate,

        /// <summary>No file yet. The default instance is shown; nothing has been written.</summary>
        NotSavedYet,

        /// <summary>The file is older than the class and will be migrated when it loads.</summary>
        NeedsMigration,

        /// <summary>The file is newer than this build understands. It must not be overwritten.</summary>
        FromNewerVersion,

        /// <summary>The file exists but could not be read.</summary>
        Unreadable
    }

    /// <summary>A save slot as the window shows it.</summary>
    public readonly struct EditorProfile
    {
        /// <summary>The slot's id, and the name of its folder.</summary>
        public readonly int Id;

        /// <summary>What to show the user.</summary>
        public readonly string DisplayName;

        /// <summary>Whether anything has actually been written to it yet.</summary>
        public readonly bool HasFiles;

        internal EditorProfile(int id, string displayName, bool hasFiles)
        {
            Id = id;
            DisplayName = displayName;
            HasFiles = hasFiles;
        }
    }

    /// <summary>One row in the window's list.</summary>
    public readonly struct SaveEntry
    {
        /// <summary>The save class.</summary>
        public readonly Type Type;

        /// <summary>The id, and the file name.</summary>
        public readonly string SaveId;

        /// <summary>How it stands relative to its file.</summary>
        public readonly SaveFileState State;

        /// <summary>Schema version found in the file, or 0 when there is none.</summary>
        public readonly int FileVersion;

        /// <summary>Schema version the class declares.</summary>
        public readonly int ClassVersion;

        internal SaveEntry(Type type, string saveId, SaveFileState state, int fileVersion, int classVersion)
        {
            Type = type;
            SaveId = saveId;
            State = state;
            FileVersion = fileVersion;
            ClassVersion = classVersion;
        }
    }

    /// <summary>
    /// Everything the save window does that is not drawing: listing saves, reading one for editing,
    /// writing it back, and the export/import/reset actions.
    /// </summary>
    /// <remarks>
    /// Kept apart from the window so it can be tested without opening any UI, and so the rules about
    /// when a file is created or written live in one place.
    /// </remarks>
    public sealed class SaveEditorSession
    {
        private readonly ISaveSerializer _serializer;
        private readonly FileSaveStorage _storage;
        private readonly MigrationPipeline _migrations;
        private readonly Dictionary<Type, int> _classVersions = new Dictionary<Type, int>();

        /// <summary>Creates a session over the project's real save folder.</summary>
        public SaveEditorSession()
            : this(new NewtonsoftSaveSerializer(), new FileSaveStorage(ImplicitSaveSettings.Instance.SaveFolderName))
        {
        }

        /// <param name="serializer">Converts saves to bytes and back.</param>
        /// <param name="storage">Where the files live.</param>
        public SaveEditorSession(ISaveSerializer serializer, FileSaveStorage storage)
        {
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _migrations = new MigrationPipeline(SaveTypeRegistry.GetMigrations());
        }

        /// <summary>Where the save files are.</summary>
        public FileSaveStorage Storage => _storage;

        /// <summary>Whether the game is running, in which case the window edits live instances.</summary>
        public static bool IsLive => EditorApplication.isPlayingOrWillChangePlaymode;

        /// <summary>Every save slot the project knows about.</summary>
        /// <remarks>
        /// Folders on disk are not the answer on their own: a slot's folder is only created when
        /// something is first written into it, so a slot the game just created would be invisible
        /// here until its first save. The index is the list of slots; the folders are only where the
        /// files land.
        /// <para>
        /// While the game runs, the answer comes from the running <see cref="SaveManager"/>, which
        /// is authoritative and needs no file read at all.
        /// </para>
        /// </remarks>
        public IReadOnlyList<EditorProfile> GetProfiles()
        {
            var byId = new Dictionary<int, string>();

            if (IsLive)
            {
                foreach (var profile in SaveManager.GetProfiles())
                {
                    byId[profile.Id] = profile.DisplayName;
                }
            }
            else
            {
                foreach (var profile in ReadIndexedProfiles())
                {
                    byId[profile.Id] = profile.DisplayName;
                }
            }

            // Folders without an index entry are still someone's saves.
            foreach (var id in _storage.ListProfileIds())
            {
                if (!byId.ContainsKey(id))
                {
                    byId[id] = "Slot " + (id + 1);
                }
            }

            if (!byId.ContainsKey(0))
            {
                byId[0] = "Slot 1";
            }

            var ids = new List<int>(byId.Keys);
            ids.Sort();

            var profiles = new List<EditorProfile>(ids.Count);

            foreach (var id in ids)
            {
                profiles.Add(new EditorProfile(id, byId[id], _storage.ProfileExists(id)));
            }

            return profiles;
        }

        /// <summary>
        /// The slot the game reads from when nothing names one explicitly.
        /// </summary>
        /// <remarks>
        /// Not the same as the slot being looked at in the window. Browsing a slot is a local
        /// choice; the active one is game state, stored in the index and shared with the running
        /// game.
        /// </remarks>
        public int GetActiveProfileId()
        {
            return IsLive ? SaveManager.ActiveProfileId : ReadActiveProfileId();
        }

        /// <summary>Creates a slot and returns its id.</summary>
        public int CreateProfile(string displayName)
        {
            if (IsLive)
            {
                return SaveManager.CreateProfile(displayName).Id;
            }

            return Profiles.Create(displayName).Id;
        }

        /// <summary>Deletes a slot and every save in it.</summary>
        public void DeleteProfile(int profileId)
        {
            if (IsLive)
            {
                SaveManager.DeleteProfile(profileId);
                return;
            }

            Profiles.Delete(profileId);
        }

        /// <summary>Copies every save of one slot over another.</summary>
        public void DuplicateProfile(int sourceProfileId, int destinationProfileId)
        {
            if (IsLive)
            {
                SaveManager.CopyProfile(sourceProfileId, destinationProfileId);
                return;
            }

            Profiles.Copy(sourceProfileId, destinationProfileId);
        }

        /// <summary>Makes a slot the active one.</summary>
        public void SetActiveProfile(int profileId)
        {
            if (IsLive)
            {
                // Goes through the manager so the running game's current slot is written first.
                SaveManager.SetActiveProfile(profileId);
                return;
            }

            Profiles.SetActive(profileId);
        }

        /// <summary>Renames a slot.</summary>
        public void RenameProfile(int profileId, string displayName)
        {
            if (IsLive)
            {
                SaveManager.RenameProfile(profileId, displayName);
                return;
            }

            Profiles.Rename(profileId, displayName);
        }

        /// <summary>
        /// A profile service over this session's storage, built only when the user actually performs
        /// a slot action.
        /// </summary>
        /// <remarks>
        /// Deliberately lazy: the service writes a default index when it finds none, and merely
        /// opening the window must not create files. Creating a slot, on the other hand, is a write
        /// the user asked for.
        /// </remarks>
        private ProfileService Profiles => _profiles ?? (_profiles = new ProfileService(_storage));

        private ProfileService _profiles;

        /// <summary>The settings the index is written with, so reading it back agrees.</summary>
        private static JsonSerializerSettings IndexSettings()
        {
            var settings = new JsonSerializerSettings
            {
                Culture = System.Globalization.CultureInfo.InvariantCulture,
                DateParseHandling = DateParseHandling.None
            };

            settings.Converters.Add(new SerializableDictionaryConverter());
            return settings;
        }

        private int ReadActiveProfileId()
        {
            if (!_storage.IndexExists())
            {
                return 0;
            }

            try
            {
                var json = Encoding.UTF8.GetString(_storage.ReadIndex());
                var index = JsonConvert.DeserializeObject<ProfileIndex>(json, IndexSettings());
                return index?.ActiveProfile ?? 0;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        /// <summary>Ids only, for callers that do not care about names.</summary>
        public IReadOnlyList<int> GetProfileIds()
        {
            var ids = new List<int>();

            foreach (var profile in GetProfiles())
            {
                ids.Add(profile.Id);
            }

            return ids;
        }

        /// <summary>
        /// Reads the slots out of <c>profiles.json</c> without creating anything.
        /// </summary>
        /// <remarks>
        /// Deliberately not through <c>ProfileService</c>: that one writes a default index when it
        /// finds none, and opening a window must not create files.
        /// </remarks>
        private IReadOnlyList<ProfileInfo> ReadIndexedProfiles()
        {
            if (!_storage.IndexExists())
            {
                return Array.Empty<ProfileInfo>();
            }

            try
            {
                var json = Encoding.UTF8.GetString(_storage.ReadIndex());
                var index = JsonConvert.DeserializeObject<ProfileIndex>(json, IndexSettings());

                return index?.Profiles ?? (IReadOnlyList<ProfileInfo>)Array.Empty<ProfileInfo>();
            }
            catch (Exception e)
            {
                // A broken index is not the window's problem to fix - the runtime rebuilds it from
                // the folders. Here it just means fewer slots to show.
                ImplicitSaveLog.Info("The profile index could not be read by the editor: " + e.Message);
                return Array.Empty<ProfileInfo>();
            }
        }

        /// <summary>
        /// Every save type in the project with the state of its file, including the ones that have
        /// never been written.
        /// </summary>
        /// <remarks>
        /// Types without a file are listed on purpose: a developer who has not pressed play yet
        /// should still see what their save looks like and be able to set it up.
        /// <para>
        /// Types from editor-only and test assemblies are left out by default. They do not exist in
        /// a build, so listing them next to the game's real saves suggests a shipped game will have
        /// files for them - and in a project with a few save fixtures they crowd out the ones that
        /// matter.
        /// </para>
        /// </remarks>
        /// <param name="profileId">Profile to inspect.</param>
        /// <param name="includeEditorOnly">Also list types that cannot be in a build.</param>
        public IReadOnlyList<SaveEntry> ListSaves(int profileId, bool includeEditorOnly = false)
        {
            var entries = new List<SaveEntry>();
            var types = includeEditorOnly
                ? SaveTypeDiscovery.FindSaveTypes()
                : SaveTypeDiscovery.FindBuildableSaveTypes();

            foreach (var type in types)
            {
                var saveId = SaveTypeDiscovery.ResolveSaveId(type);
                var classVersion = GetClassVersion(type);

                if (!_storage.Exists(profileId, saveId))
                {
                    entries.Add(new SaveEntry(type, saveId, SaveFileState.NotSavedYet, 0, classVersion));
                    continue;
                }

                try
                {
                    var envelope = _serializer.ReadEnvelope(_storage.Read(profileId, saveId));
                    var state = envelope.SchemaVersion > classVersion
                        ? SaveFileState.FromNewerVersion
                        : envelope.SchemaVersion < classVersion
                            ? SaveFileState.NeedsMigration
                            : SaveFileState.UpToDate;

                    entries.Add(new SaveEntry(type, saveId, state, envelope.SchemaVersion, classVersion));
                }
                catch (SaveException)
                {
                    entries.Add(new SaveEntry(type, saveId, SaveFileState.Unreadable, 0, classVersion));
                }
            }

            entries.Sort((a, b) => string.CompareOrdinal(a.SaveId, b.SaveId));
            return entries;
        }

        /// <summary>
        /// Produces the instance to edit. Reading never creates a file: a type with no save yet
        /// yields a default instance, and nothing is written until the user actually applies a
        /// change.
        /// </summary>
        /// <remarks>
        /// Opening a window is a read. Writing to the player's save folder as a side effect of
        /// looking at it would leave junk behind for every type later renamed or deleted.
        /// <para>
        /// While the game is running this returns the <b>live instance</b> instead, because editing
        /// the file underneath a running game would just be overwritten by the next autosave tick.
        /// </para>
        /// </remarks>
        public SaveData Load(Type type, int profileId, out bool isLive)
        {
            isLive = IsLive;

            if (isLive)
            {
                return SaveManager.Get(type, profileId);
            }

            var saveId = SaveTypeDiscovery.ResolveSaveId(type);

            if (!_storage.Exists(profileId, saveId))
            {
                var fresh = (SaveData)Activator.CreateInstance(type);
                fresh.ResetToDefaults();
                fresh.ProfileId = profileId;
                return fresh;
            }

            var envelope = _serializer.ReadEnvelope(_storage.Read(profileId, saveId));
            var classVersion = GetClassVersion(type);

            if (envelope.SchemaVersion > classVersion)
            {
                var placeholder = (SaveData)Activator.CreateInstance(type);
                placeholder.ResetToDefaults();
                placeholder.ProfileId = profileId;
                placeholder.IsReadOnly = true;
                return placeholder;
            }

            var payload = envelope.SchemaVersion < classVersion
                ? _migrations.Migrate(type, envelope.Data, envelope.SchemaVersion, classVersion)
                : envelope.Data;

            var instance = _serializer.DeserializePayload(payload, type);
            instance.ProfileId = profileId;
            return instance;
        }

        /// <summary>
        /// Writes the edited instance. In play mode it only flags the live instance, leaving the
        /// runtime to persist it on its own schedule.
        /// </summary>
        /// <exception cref="SaveException">The save is read-only, or the write failed.</exception>
        public void Apply(SaveData data, int profileId)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (data.IsReadOnly)
            {
                throw new SaveException(
                    "This save was written by a newer version of the game. Writing over it would destroy " +
                    "progress this build cannot represent.");
            }

            if (IsLive)
            {
                // The instance the window edited is the one the game is holding, so the change is
                // already in effect; marking it dirty is what gets it written.
                data.MarkDirty();
                SaveManager.Save(data.GetType(), profileId);
                return;
            }

            var saveId = SaveTypeDiscovery.ResolveSaveId(data.GetType());
            _storage.Write(profileId, saveId, _serializer.Serialize(data, saveId));
        }

        /// <summary>Deletes a save file and its backup.</summary>
        public void Delete(Type type, int profileId)
        {
            _storage.Delete(profileId, SaveTypeDiscovery.ResolveSaveId(type));
        }

        /// <summary>The file's contents as text, for export or for showing the raw JSON.</summary>
        public string ReadRawJson(Type type, int profileId)
        {
            var saveId = SaveTypeDiscovery.ResolveSaveId(type);

            return _storage.Exists(profileId, saveId)
                ? Encoding.UTF8.GetString(_storage.Read(profileId, saveId))
                : null;
        }

        /// <summary>Writes a save file straight from JSON text.</summary>
        /// <exception cref="SaveException">The text is not a valid save file for this type.</exception>
        public void ImportRawJson(Type type, int profileId, string json)
        {
            var bytes = Encoding.UTF8.GetBytes(json ?? string.Empty);

            // Parsed before it is written, so a bad paste cannot replace a good save.
            var envelope = _serializer.ReadEnvelope(bytes);
            var expectedId = SaveTypeDiscovery.ResolveSaveId(type);

            if (!string.Equals(envelope.SaveId, expectedId, StringComparison.Ordinal))
            {
                throw new SaveException(
                    $"That file is a '{envelope.SaveId}' save, not a '{expectedId}' one. Importing it here " +
                    "would put the wrong data under the wrong name.");
            }

            _storage.Write(profileId, expectedId, bytes);
        }

        /// <summary>Opens the save folder in the system file browser.</summary>
        public void RevealSaveFolder(int profileId)
        {
            var path = _storage.ProfileExists(profileId) ? _storage.GetProfilePath(profileId) : _storage.RootPath;
            Directory.CreateDirectory(path);
            EditorUtility.RevealInFinder(path);
        }

        private int GetClassVersion(Type type)
        {
            if (_classVersions.TryGetValue(type, out var version))
            {
                return version;
            }

            version = ((SaveData)Activator.CreateInstance(type)).SchemaVersion;
            _classVersions[type] = version;
            return version;
        }
    }
}
