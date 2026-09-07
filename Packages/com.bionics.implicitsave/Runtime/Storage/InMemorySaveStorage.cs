using System;
using System.Collections.Generic;

namespace ImplicitSave.Storage
{
    /// <summary>
    /// Keeps saves in memory. Used by the test suite so the whole system can be exercised without
    /// touching a disk, and useful in-game for a run that should not persist.
    /// </summary>
    /// <remarks>
    /// It mirrors the file backend's semantics on purpose - backup on overwrite, quarantine instead
    /// of delete - so a test that passes here means something about the real one.
    /// </remarks>
    public sealed class InMemorySaveStorage : ISaveStorage
    {
        private readonly Dictionary<string, byte[]> _entries = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> _backups = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        private readonly Dictionary<string, byte[]> _quarantined = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        private byte[] _index;

        /// <summary>
        /// How many save writes have been performed. The dirty tracker's whole point is keeping
        /// this number from growing when nothing changed, which is what the tests assert against.
        /// </summary>
        /// <remarks>
        /// Counts saves only. The profile index is written on its own schedule - when a slot is
        /// created, renamed or made active - and mixing it in here would make the number mean
        /// nothing.
        /// </remarks>
        public int WriteCount { get; private set; }

        /// <summary>How many times the profile index has been written.</summary>
        public int IndexWriteCount { get; private set; }

        /// <summary>Entries moved aside by <see cref="Quarantine"/>, keyed by their reported path.</summary>
        public IReadOnlyDictionary<string, byte[]> Quarantined => _quarantined;

        /// <inheritdoc />
        public bool Exists(int profileId, string saveId)
        {
            return _entries.ContainsKey(GetKey(profileId, saveId));
        }

        /// <inheritdoc />
        public byte[] Read(int profileId, string saveId)
        {
            var key = GetKey(profileId, saveId);
            if (!_entries.TryGetValue(key, out var content))
            {
                throw new SaveStorageException($"No entry '{key}' in memory storage.", null);
            }

            return Copy(content);
        }

        /// <inheritdoc />
        public bool TryReadBackup(int profileId, string saveId, out byte[] content)
        {
            if (_backups.TryGetValue(GetKey(profileId, saveId), out var stored))
            {
                content = Copy(stored);
                return true;
            }

            content = null;
            return false;
        }

        /// <inheritdoc />
        public void Write(int profileId, string saveId, byte[] content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            var key = GetKey(profileId, saveId);

            if (_entries.TryGetValue(key, out var previous))
            {
                _backups[key] = previous;
            }

            // Copy on the way in and out: callers must not be able to mutate stored bytes by
            // holding on to the array they handed over.
            _entries[key] = Copy(content);
            WriteCount++;
        }

        /// <inheritdoc />
        public string Quarantine(int profileId, string saveId)
        {
            var key = GetKey(profileId, saveId);
            if (!_entries.TryGetValue(key, out var content))
            {
                return null;
            }

            var target = key + ".corrupt";
            _quarantined[target] = content;
            _entries.Remove(key);
            return target;
        }

        /// <inheritdoc />
        public void Delete(int profileId, string saveId)
        {
            var key = GetKey(profileId, saveId);
            _entries.Remove(key);
            _backups.Remove(key);
        }

        /// <inheritdoc />
        public IReadOnlyList<string> ListSaveIds(int profileId)
        {
            var prefix = profileId + "/";
            var ids = new List<string>();

            foreach (var key in _entries.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    ids.Add(key.Substring(prefix.Length));
                }
            }

            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        /// <inheritdoc />
        public bool IndexExists()
        {
            return _index != null;
        }

        /// <inheritdoc />
        public byte[] ReadIndex()
        {
            if (_index == null)
            {
                throw new SaveStorageException("No profile index in memory storage.", null);
            }

            return Copy(_index);
        }

        /// <inheritdoc />
        public void WriteIndex(byte[] content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            _index = Copy(content);
            IndexWriteCount++;
        }

        /// <inheritdoc />
        public string QuarantineIndex()
        {
            if (_index == null)
            {
                return null;
            }

            const string target = "profiles.corrupt";
            _quarantined[target] = _index;
            _index = null;
            return target;
        }

        /// <inheritdoc />
        public IReadOnlyList<int> ListProfileIds()
        {
            var ids = new List<int>();

            foreach (var key in _entries.Keys)
            {
                var separator = key.IndexOf('/');
                if (separator > 0 && int.TryParse(key.Substring(0, separator), out var id) && !ids.Contains(id))
                {
                    ids.Add(id);
                }
            }

            ids.Sort();
            return ids;
        }

        /// <inheritdoc />
        public bool ProfileExists(int profileId)
        {
            var prefix = profileId + "/";

            foreach (var key in _entries.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <inheritdoc />
        public void DeleteProfile(int profileId)
        {
            foreach (var saveId in ListSaveIds(profileId))
            {
                Delete(profileId, saveId);
            }
        }

        /// <inheritdoc />
        public void CopyProfile(int sourceProfileId, int destinationProfileId)
        {
            if (sourceProfileId == destinationProfileId)
            {
                return;
            }

            if (!ProfileExists(sourceProfileId))
            {
                throw new SaveStorageException($"Profile {sourceProfileId} has nothing to copy.", null);
            }

            DeleteProfile(destinationProfileId);

            foreach (var saveId in ListSaveIds(sourceProfileId))
            {
                _entries[GetKey(destinationProfileId, saveId)] = Copy(_entries[GetKey(sourceProfileId, saveId)]);
            }
        }

        /// <summary>Empties the storage, including backups and quarantined entries.</summary>
        public void Clear()
        {
            _entries.Clear();
            _backups.Clear();
            _quarantined.Clear();
            _index = null;
            WriteCount = 0;
            IndexWriteCount = 0;
        }

        /// <summary>
        /// Replaces an entry's bytes without going through <see cref="Write"/>, so a test can put
        /// unreadable content where a valid save should be.
        /// </summary>
        public void Corrupt(int profileId, string saveId, byte[] content)
        {
            _entries[GetKey(profileId, saveId)] = Copy(content);
        }

        private static string GetKey(int profileId, string saveId)
        {
            return profileId + "/" + saveId;
        }

        private static byte[] Copy(byte[] source)
        {
            if (source == null)
            {
                return null;
            }

            var copy = new byte[source.Length];
            Buffer.BlockCopy(source, 0, copy, 0, source.Length);
            return copy;
        }
    }
}
