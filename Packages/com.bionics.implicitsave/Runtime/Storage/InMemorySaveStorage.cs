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

        /// <summary>
        /// How many writes have been performed. The dirty tracker's whole point is keeping this
        /// number from growing when nothing changed, which is what the tests assert against.
        /// </summary>
        public int WriteCount { get; private set; }

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

        /// <summary>Empties the storage, including backups and quarantined entries.</summary>
        public void Clear()
        {
            _entries.Clear();
            _backups.Clear();
            _quarantined.Clear();
            WriteCount = 0;
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
