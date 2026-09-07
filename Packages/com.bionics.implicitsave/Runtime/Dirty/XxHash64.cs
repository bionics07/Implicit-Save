namespace ImplicitSave
{
    /// <summary>
    /// A fast, non-cryptographic 64-bit hash, used only to tell whether a save's bytes changed
    /// since the last write.
    /// </summary>
    /// <remarks>
    /// This is change detection, not security. SHA would be the wrong tool: it is far slower, and
    /// nothing here is defending against anyone forging a value.
    /// <para>
    /// The hash never leaves memory. It is not written to a save file, not compared across
    /// sessions, and not sent anywhere - each value is only ever compared with another value this
    /// same process produced. That is why the exact algorithm is not load-bearing: it needs to be
    /// deterministic within a run and spread values well, and nothing more.
    /// </para>
    /// </remarks>
    internal static class XxHash64
    {
        private const ulong Prime1 = 11400714785074694791UL;
        private const ulong Prime2 = 14029467366897019727UL;
        private const ulong Prime3 = 1609587929392839161UL;
        private const ulong Prime4 = 9650029242287828579UL;
        private const ulong Prime5 = 2870177450012600261UL;

        /// <summary>Hashes a byte range.</summary>
        internal static ulong Compute(byte[] data, ulong seed = 0)
        {
            if (data == null)
            {
                return seed;
            }

            var length = data.Length;
            var index = 0;
            ulong hash;

            if (length >= 32)
            {
                var v1 = seed + Prime1 + Prime2;
                var v2 = seed + Prime2;
                var v3 = seed;
                var v4 = seed - Prime1;

                var limit = length - 32;
                do
                {
                    v1 = Round(v1, ReadUInt64(data, index));
                    v2 = Round(v2, ReadUInt64(data, index + 8));
                    v3 = Round(v3, ReadUInt64(data, index + 16));
                    v4 = Round(v4, ReadUInt64(data, index + 24));
                    index += 32;
                }
                while (index <= limit);

                hash = RotateLeft(v1, 1) + RotateLeft(v2, 7) + RotateLeft(v3, 12) + RotateLeft(v4, 18);
                hash = MergeRound(hash, v1);
                hash = MergeRound(hash, v2);
                hash = MergeRound(hash, v3);
                hash = MergeRound(hash, v4);
            }
            else
            {
                hash = seed + Prime5;
            }

            hash += (ulong)length;

            while (index + 8 <= length)
            {
                hash ^= Round(0, ReadUInt64(data, index));
                hash = (RotateLeft(hash, 27) * Prime1) + Prime4;
                index += 8;
            }

            if (index + 4 <= length)
            {
                hash ^= ReadUInt32(data, index) * Prime1;
                hash = (RotateLeft(hash, 23) * Prime2) + Prime3;
                index += 4;
            }

            while (index < length)
            {
                hash ^= data[index] * Prime5;
                hash = RotateLeft(hash, 11) * Prime1;
                index++;
            }

            hash ^= hash >> 33;
            hash *= Prime2;
            hash ^= hash >> 29;
            hash *= Prime3;
            hash ^= hash >> 32;

            return hash;
        }

        private static ulong Round(ulong accumulator, ulong input)
        {
            accumulator += input * Prime2;
            accumulator = RotateLeft(accumulator, 31);
            accumulator *= Prime1;
            return accumulator;
        }

        private static ulong MergeRound(ulong hash, ulong value)
        {
            hash ^= Round(0, value);
            hash = (hash * Prime1) + Prime4;
            return hash;
        }

        private static ulong RotateLeft(ulong value, int count)
        {
            return (value << count) | (value >> (64 - count));
        }

        private static ulong ReadUInt64(byte[] data, int index)
        {
            return data[index]
                   | ((ulong)data[index + 1] << 8)
                   | ((ulong)data[index + 2] << 16)
                   | ((ulong)data[index + 3] << 24)
                   | ((ulong)data[index + 4] << 32)
                   | ((ulong)data[index + 5] << 40)
                   | ((ulong)data[index + 6] << 48)
                   | ((ulong)data[index + 7] << 56);
        }

        private static uint ReadUInt32(byte[] data, int index)
        {
            return data[index]
                   | ((uint)data[index + 1] << 8)
                   | ((uint)data[index + 2] << 16)
                   | ((uint)data[index + 3] << 24);
        }
    }
}
