namespace ImplicitSave.Samples.MultiProfile
{
    /// <summary>
    /// One save type, stored once per profile. A profile is a save slot: the same class gets its own
    /// file inside each slot's folder, so nothing about the class changes to support several slots.
    /// </summary>
    [SaveId(Id)]
    public class SlotSaveData : SaveData
    {
        /// <summary>The file name. The prefix only keeps it apart from your own game's saves.</summary>
        public const string Id = "sample_profiles_slot";

        /// <summary>Name the player typed for this slot's hero.</summary>
        public string HeroName = "";

        /// <summary>Hero level.</summary>
        public int Level = 1;

        /// <summary>Gold carried.</summary>
        public int Gold;

        /// <inheritdoc />
        public override void ResetToDefaults()
        {
            HeroName = "";
            Level = 1;
            Gold = 0;
        }
    }
}
