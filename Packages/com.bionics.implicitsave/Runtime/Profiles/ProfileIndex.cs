using System.Collections.Generic;
using Newtonsoft.Json;

namespace ImplicitSave
{
    /// <summary>
    /// The shape of <c>profiles.json</c>: which slot is active and what the slots are.
    /// </summary>
    /// <remarks>
    /// Package bookkeeping, not save data, so it is written with ordinary Newtonsoft rules rather
    /// than Unity's. It sits beside the profile folders instead of inside one, because it describes
    /// all of them.
    /// </remarks>
    internal sealed class ProfileIndex
    {
        [JsonProperty("activeProfile")]
        internal int ActiveProfile { get; set; }

        [JsonProperty("profiles")]
        internal List<ProfileInfo> Profiles { get; set; }
    }
}
