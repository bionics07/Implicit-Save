using System;
using System.Globalization;

namespace ImplicitSave
{
    /// <summary>
    /// The one place timestamps are formatted. Every time written by the package is UTC and ISO
    /// 8601, so a save written in one time zone reads correctly in another.
    /// </summary>
    internal static class SaveTimestamp
    {
        private const string Format = "yyyy-MM-ddTHH:mm:ssZ";

        /// <summary>The current time, formatted for a save file.</summary>
        internal static string UtcNow()
        {
            return DateTime.UtcNow.ToString(Format, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parses a timestamp the package wrote. Returns <c>false</c> rather than throwing, because
        /// a bad timestamp in an index is a cosmetic problem, not a reason to lose a save.
        /// </summary>
        internal static bool TryParse(string value, out DateTime utc)
        {
            return DateTime.TryParseExact(
                value,
                Format,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal,
                out utc);
        }
    }
}
