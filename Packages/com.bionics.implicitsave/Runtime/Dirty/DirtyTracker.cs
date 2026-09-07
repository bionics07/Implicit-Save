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

        /// <summary>Hash of a save's serialized bytes.</summary>
        public static ulong ComputeHash(byte[] content)
        {
            return XxHash64.Compute(content);
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
