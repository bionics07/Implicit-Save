using System;
using System.Collections.Generic;

namespace ImplicitSave
{
    /// <summary>
    /// Remembers what was last written for each save, so an autosave tick that finds nothing new
    /// writes nothing at all.
    /// </summary>
    /// <remarks>
    /// The default strategy compares a hash of the serialized bytes. That is correct by
    /// construction: it notices a change no matter where it happened, including
    /// <c>Inventory["potion"] = 5</c> or <c>list[0].Quantity++</c>, which a manual dirty flag or a
    /// generated property setter would both miss. Most save mutations happen inside collections, so
    /// missing them is missing the common case.
    /// <para>
    /// The cost is one serialization pass per live save per tick, against the disk write it avoids.
    /// </para>
    /// </remarks>
    public sealed class DirtyTracker
    {
        private readonly Dictionary<Key, ulong> _lastWritten = new Dictionary<Key, ulong>();

        /// <summary>Hash of a save's serialized bytes, exactly as they are.</summary>
        public static ulong ComputeHash(byte[] content)
        {
            return XxHash64.Compute(content);
        }

        /// <summary>
        /// Hash of what a save CONTAINS, ignoring the moment it was serialized.
        /// </summary>
        /// <remarks>
        /// This exists because hashing the file as written made dirty tracking useless. The envelope
        /// carries <c>$savedAt</c>, stamped from the clock every time the save is serialized, so two
        /// passes over an untouched object produced different bytes and every autosave tick wrote to
        /// disk - which is the exact opposite of the feature.
        /// <para>
        /// It hid well: the timestamp has one-second resolution, so a test suite running in
        /// milliseconds always saw the same value and passed. It takes a human clicking twice, a
        /// second apart, to see it.
        /// </para>
        /// <para>
        /// The value is blanked rather than removed so every other byte keeps its position, and the
        /// search is by key rather than by offset so the envelope's field order stays free to change.
        /// A payload with no such field hashes whole, which is simply the old behaviour.
        /// </para>
        /// </remarks>
        public static ulong ComputeContentHash(byte[] content)
        {
            if (content == null)
            {
                return 0;
            }

            var start = FindTimestampValue(content, out var length);

            if (start < 0)
            {
                return XxHash64.Compute(content);
            }

            var stable = (byte[])content.Clone();

            for (var i = start; i < start + length; i++)
            {
                stable[i] = (byte)'0';
            }

            return XxHash64.Compute(stable);
        }

        /// <summary>
        /// Locates the characters between the quotes of the <c>$savedAt</c> value.
        /// </summary>
        /// <returns>Where the value starts, or -1 when the field is not there.</returns>
        private static int FindTimestampValue(byte[] content, out int length)
        {
            length = 0;

            var key = System.Text.Encoding.UTF8.GetBytes(Serialization.SaveEnvelope.SavedAtKey);
            var keyAt = IndexOf(content, key);

            if (keyAt < 0)
            {
                return -1;
            }

            // Past the key, its closing quote and the colon, to the quote that opens the value.
            var cursor = keyAt + key.Length;

            while (cursor < content.Length && content[cursor] != (byte)':')
            {
                cursor++;
            }

            while (cursor < content.Length && content[cursor] != (byte)'"')
            {
                cursor++;
            }

            if (cursor >= content.Length)
            {
                return -1;
            }

            var valueStart = cursor + 1;
            var valueEnd = valueStart;

            while (valueEnd < content.Length && content[valueEnd] != (byte)'"')
            {
                valueEnd++;
            }

            if (valueEnd >= content.Length)
            {
                return -1;
            }

            length = valueEnd - valueStart;
            return valueStart;
        }

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            for (var i = 0; i <= haystack.Length - needle.Length; i++)
            {
                var found = true;

                for (var j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        found = false;
                        break;
                    }
                }

                if (found)
                {
                    return i;
                }
            }

            return -1;
        }

        /// <summary>
        /// Whether these bytes are the same as what was last written for this save.
        /// </summary>
        public bool IsUnchanged(Type type, int profileId, ulong hash)
        {
            return _lastWritten.TryGetValue(new Key(type, profileId), out var previous) && previous == hash;
        }

        /// <summary>Records what was just written.</summary>
        public void Record(Type type, int profileId, ulong hash)
        {
            _lastWritten[new Key(type, profileId)] = hash;
        }

        /// <summary>
        /// Forgets a save, so the next write happens regardless. Used when an instance is dropped
        /// from memory - the next one loaded is a different object and must be written once.
        /// </summary>
        public void Forget(Type type, int profileId)
        {
            _lastWritten.Remove(new Key(type, profileId));
        }

        /// <summary>Forgets every save of a profile.</summary>
        public void ForgetProfile(int profileId)
        {
            var stale = new List<Key>();

            foreach (var key in _lastWritten.Keys)
            {
                if (key.ProfileId == profileId)
                {
                    stale.Add(key);
                }
            }

            foreach (var key in stale)
            {
                _lastWritten.Remove(key);
            }
        }

        /// <summary>Forgets everything.</summary>
        public void Clear()
        {
            _lastWritten.Clear();
        }

        private readonly struct Key : IEquatable<Key>
        {
            private readonly Type _type;
            internal readonly int ProfileId;

            internal Key(Type type, int profileId)
            {
                _type = type;
                ProfileId = profileId;
            }

            public bool Equals(Key other)
            {
                return ProfileId == other.ProfileId && _type == other._type;
            }

            public override bool Equals(object obj)
            {
                return obj is Key other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    return ((_type?.GetHashCode() ?? 0) * 397) ^ ProfileId;
                }
            }
        }
    }
}
