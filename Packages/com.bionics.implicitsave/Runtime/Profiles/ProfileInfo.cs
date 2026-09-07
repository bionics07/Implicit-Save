using System;
using Newtonsoft.Json;

namespace ImplicitSave
{
    /// <summary>
    /// What a game needs to know about a save slot without opening it: its name, when it was last
    /// played, how long it has been played, and whatever the game itself wants to show.
    /// </summary>
    /// <remarks>
    /// The point of this type is that a "load game" screen can list every slot without loading a
    /// single save file. Reading five full saves to draw five buttons is how load screens get slow.
    /// </remarks>
    [Serializable]
    public class ProfileInfo
    {
        /// <summary>The profile's id, and the name of its folder on disk.</summary>
        [JsonProperty("id")] public int Id;

        /// <summary>What to show the player. Defaults to "Slot N".</summary>
        [JsonProperty("displayName")] public string DisplayName;

        /// <summary>When the profile was created, ISO 8601 UTC.</summary>
        [JsonProperty("createdAt")] public string CreatedAt;

        /// <summary>When the profile was last made active, ISO 8601 UTC.</summary>
        [JsonProperty("lastPlayedAt")] public string LastPlayedAt;

        /// <summary>Total play time, in seconds. The game decides what counts as playing.</summary>
        [JsonProperty("playTimeSeconds")] public double PlayTimeSeconds;

        /// <summary>
        /// Anything the game wants on the slot button - the current level, a completion percentage,
        /// the hero's name. Kept as text so the index never depends on a game type.
        /// </summary>
        [JsonProperty("custom")] public SerializableDictionary<string, string> Custom =
            new SerializableDictionary<string, string>();

        /// <summary>Creates an empty profile. Needed by the serializer.</summary>
        public ProfileInfo()
        {
        }

        /// <summary>Creates a profile with the given id and name.</summary>
        public ProfileInfo(int id, string displayName)
        {
            Id = id;
            DisplayName = displayName;
            CreatedAt = SaveTimestamp.UtcNow();
            LastPlayedAt = CreatedAt;
        }
    }
}
