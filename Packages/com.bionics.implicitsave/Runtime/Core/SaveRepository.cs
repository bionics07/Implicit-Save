using System;
using System.Collections.Generic;
using ImplicitSave.Serialization;
using ImplicitSave.Storage;

namespace ImplicitSave
{
    /// <summary>
    /// Owns the live save instances: one per profile and type, loaded on first use and written back
    /// on demand.
    /// </summary>
    /// <remarks>
    /// This is where the real work happens - the public <c>SaveManager</c> facade of a later phase
    /// only forwards to it.
    /// <para>
    /// Main thread only. Save instances may hold Unity types and the game may mutate them from
    /// anywhere, so reading one off-thread is a data race.
    /// </para>
    /// </remarks>
    public sealed class SaveRepository
    {
        private readonly ISaveSerializer _serializer;
        private readonly ISaveStorage _storage;
        private readonly Dictionary<SaveKey, SaveData> _instances = new Dictionary<SaveKey, SaveData>();
        private readonly DirtyTracker _dirtyTracker = new DirtyTracker();
        private readonly SaveWriteQueue _writeQueue;

        /// <summary>
        /// Raised when a save could not be loaded or written. A game listens to this to tell the
        /// player something went wrong rather than failing silently.
        /// </summary>
        public event Action<SaveException> Failed;

        /// <summary>Raised after a save is written, with its type and profile.</summary>
        public event Action<Type, int> Saved;

        /// <summary>Raised after a save is read from storage, with its type and profile.</summary>
        public event Action<Type, int> Loaded;

        /// <summary>Where bytes are read from and written to.</summary>
        public ISaveStorage Storage => _storage;

        /// <param name="serializer">Converts instances to bytes and back.</param>
        /// <param name="storage">Where those bytes live.</param>
        public SaveRepository(ISaveSerializer serializer, ISaveStorage storage)
        {
            _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            _writeQueue = new SaveWriteQueue(storage);
            _writeQueue.Failed += e => Failed?.Invoke(e);
        }

        /// <summary>Tracks what was last written, so an unchanged save is not rewritten.</summary>
        public DirtyTracker DirtyTracker => _dirtyTracker;

        /// <summary>The queue background writes go through.</summary>
        public SaveWriteQueue WriteQueue => _writeQueue;

        /// <summary>
        /// Returns the live instance for this profile, loading it on first use. Never returns null:
        /// a missing file yields a fresh instance, an unreadable one yields its backup or a fresh
        /// instance.
        /// </summary>
        /// <typeparam name="T">The save type to fetch.</typeparam>
        /// <param name="profileId">Profile to read from.</param>
        public T Get<T>(int profileId) where T : SaveData, new()
        {
            return (T)Get(typeof(T), profileId);
        }

        /// <summary>
        /// Type-erased form of <see cref="Get{T}"/>, for callers that only have a
        /// <see cref="Type"/> - the editor window, and the registry.
        /// </summary>
        /// <exception cref="ArgumentException"><paramref name="type"/> is not a concrete save type.</exception>
        public SaveData Get(Type type, int profileId)
        {
            var key = new SaveKey(type, profileId);
            if (_instances.TryGetValue(key, out var cached))
            {
                return cached;
            }

            var instance = Load(type, profileId);
            instance.ProfileId = profileId;
            instance.IsDirty = false;
            _instances[key] = instance;
            return instance;
        }

        /// <summary>Whether an instance of this type is already in memory for this profile.</summary>
        public bool IsLoaded(Type type, int profileId)
        {
            return _instances.ContainsKey(new SaveKey(type, profileId));
        }

        /// <summary>Writes one loaded save to storage. Does nothing if it was never loaded.</summary>
        public void Save<T>(int profileId) where T : SaveData
        {
            Save(typeof(T), profileId);
        }

        /// <summary>Writes one loaded save to storage. Does nothing if it was never loaded.</summary>
        /// <param name="type">The save type.</param>
        /// <param name="profileId">Profile to write.</param>
        /// <param name="force">Write even when nothing changed since the last write.</param>
        /// <param name="async">Hand the write to the background queue instead of blocking.</param>
        public void Save(Type type, int profileId, bool force = true, bool async = false)
        {
            if (!_instances.TryGetValue(new SaveKey(type, profileId), out var instance))
            {
                return;
            }

            Write(instance, type, profileId, force, async);
        }

        /// <summary>Writes every loaded save of a profile.</summary>
        /// <param name="profileId">Profile to write.</param>
        /// <param name="force">Write even the saves that did not change.</param>
        /// <param name="async">Hand the writes to the background queue instead of blocking.</param>
        public void SaveAll(int profileId, bool force = true, bool async = false)
        {
            // Copied because a failing write raises Failed, and a handler may touch the repository.
            foreach (var pair in new List<KeyValuePair<SaveKey, SaveData>>(_instances))
            {
                if (pair.Key.ProfileId == profileId)
                {
                    Write(pair.Value, pair.Key.Type, profileId, force, async);
                }
            }
        }

        /// <summary>Writes every loaded save of every profile.</summary>
        public void SaveEverything(bool force = true, bool async = false)
        {
            foreach (var pair in new List<KeyValuePair<SaveKey, SaveData>>(_instances))
            {
                Write(pair.Value, pair.Key.Type, pair.Key.ProfileId, force, async);
            }
        }

        /// <summary>Blocks until every queued write has finished.</summary>
        public bool Flush(TimeSpan timeout)
        {
            return _writeQueue.Drain(timeout);
        }

        /// <summary>
        /// Drops a profile's instances from memory so the next <see cref="Get{T}"/> reads from
        /// storage again. Unwritten changes are lost, which is the point.
        /// </summary>
        public void Reload(int profileId)
        {
            var stale = new List<SaveKey>();

            foreach (var key in _instances.Keys)
            {
                if (key.ProfileId == profileId)
                {
                    stale.Add(key);
                }
            }

            foreach (var key in stale)
            {
                _instances.Remove(key);
            }

            // The next instance loaded is a different object, so it has to be written once even if
            // its bytes happen to match what the dropped one last wrote.
            _dirtyTracker.ForgetProfile(profileId);
        }

        /// <summary>Drops every instance from memory without writing.</summary>
        public void Clear()
        {
            _instances.Clear();
            _dirtyTracker.Clear();
        }

        private void Write(SaveData instance, Type type, int profileId, bool force, bool async)
        {
            var saveId = SaveIdResolver.Resolve(type);

            try
            {
                // Always on the main thread: the instance may hold Unity types and the game may be
                // mutating it, so serializing anywhere else is a data race.
                instance.OnBeforeSave();
                var bytes = _serializer.Serialize(instance, saveId);

                if (!force && !ShouldWrite(instance, type, profileId, bytes))
                {
                    return;
                }

                // The bytes from the change check are the bytes written - a tick serializes once,
                // not twice.
                if (async)
                {
                    _writeQueue.Enqueue(profileId, saveId, bytes);
                }
                else
                {
                    _storage.Write(profileId, saveId, bytes);
                }

                _dirtyTracker.Record(type, profileId, DirtyTracker.ComputeHash(bytes));
                instance.IsDirty = false;
                ImplicitSaveLog.Info($"Wrote '{saveId}' of profile {profileId} ({bytes.Length} bytes).");
                Saved?.Invoke(type, profileId);
            }
            catch (SaveException e)
            {
                ImplicitSaveLog.Error($"Failed to write '{saveId}' of profile {profileId}: {e.Message}");
                Failed?.Invoke(e);
            }
        }

        /// <summary>Whether this save differs from what was last written.</summary>
        private bool ShouldWrite(SaveData instance, Type type, int profileId, byte[] bytes)
        {
            var strategy = ImplicitSaveSettings.Instance.DirtyStrategy;

            if (strategy == DirtyStrategy.ManualFlag)
            {
                return instance.IsDirty;
            }

            // Both: the flag is a fast path that skips hashing when the answer is already known.
            if (strategy == DirtyStrategy.Both && instance.IsDirty)
            {
                return true;
            }

            return !_dirtyTracker.IsUnchanged(type, profileId, DirtyTracker.ComputeHash(bytes));
        }

        private SaveData Load(Type type, int profileId)
        {
            var saveId = SaveIdResolver.Resolve(type);

            if (!_storage.Exists(profileId, saveId))
            {
                return CreateFresh(type);
            }

            try
            {
                var instance = _serializer.Deserialize(_storage.Read(profileId, saveId), type);
                instance.OnAfterLoad();
                ImplicitSaveLog.Info($"Loaded '{saveId}' of profile {profileId}.");
                Loaded?.Invoke(type, profileId);
                return instance;
            }
            catch (SaveException e)
            {
                return Recover(type, profileId, saveId, e);
            }
        }

        /// <summary>
        /// Last resort for a file that would not parse: try the backup the previous write left, and
        /// failing that move the file aside and start clean. The unreadable file is never deleted -
        /// it may be the player's only copy, and a support ticket can still ask for it.
        /// </summary>
        private SaveData Recover(Type type, int profileId, string saveId, SaveException failure)
        {
            ImplicitSaveLog.Warning(
                $"'{saveId}' of profile {profileId} could not be read ({failure.Message}). Trying its backup.");

            if (_storage.TryReadBackup(profileId, saveId, out var backup))
            {
                try
                {
                    var instance = _serializer.Deserialize(backup, type);
                    instance.OnAfterLoad();
                    ImplicitSaveLog.Warning($"Recovered '{saveId}' of profile {profileId} from its backup.");
                    return instance;
                }
                catch (SaveException e)
                {
                    ImplicitSaveLog.Warning($"The backup of '{saveId}' was unreadable too: {e.Message}");
                }
            }

            var quarantinedPath = _storage.Quarantine(profileId, saveId);
            var corrupted = new SaveCorruptedException(saveId, profileId, quarantinedPath, failure);
            ImplicitSaveLog.Error(corrupted.Message);
            Failed?.Invoke(corrupted);

            return CreateFresh(type);
        }

        private static SaveData CreateFresh(Type type)
        {
            if (!typeof(SaveData).IsAssignableFrom(type) || type.IsAbstract)
            {
                throw new ArgumentException(
                    $"'{type.Name}' is not a concrete {nameof(SaveData)} type.", nameof(type));
            }

            SaveData instance;
            try
            {
                instance = (SaveData)Activator.CreateInstance(type);
            }
            catch (Exception e)
            {
                throw new SaveException(
                    $"'{type.Name}' could not be created. Save types need a public parameterless " +
                    "constructor.", e);
            }

            instance.ResetToDefaults();
            return instance;
        }

        /// <summary>Identity of a live instance: one per type per profile.</summary>
        private readonly struct SaveKey : IEquatable<SaveKey>
        {
            internal readonly Type Type;
            internal readonly int ProfileId;

            internal SaveKey(Type type, int profileId)
            {
                Type = type ?? throw new ArgumentNullException(nameof(type));
                ProfileId = profileId;
            }

            public bool Equals(SaveKey other)
            {
                return ProfileId == other.ProfileId && Type == other.Type;
            }

            public override bool Equals(object obj)
            {
                return obj is SaveKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return (Type.GetHashCode() * 397) ^ ProfileId;
                }
            }
        }
    }
}
