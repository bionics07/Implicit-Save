using System;
using System.Collections.Generic;

namespace ImplicitSave.Tests
{
    /// <summary>
    /// Save types the tests round-trip. They deliberately stay inside Unity's serializable subset,
    /// because anything outside it is what the validator of a later phase exists to reject.
    /// </summary>
    [SaveId("items")]
    public class ItemsSaveData : SaveData
    {
        public int SelectedSlot;
        public string LastPickedUp;
        public List<string> Owned = new List<string>();
        public NestedStats Stats = new NestedStats();

        public override void ResetToDefaults()
        {
            SelectedSlot = 0;
            LastPickedUp = null;
            Owned = new List<string>();
            Stats = new NestedStats();
        }
    }

    /// <summary>A plain nested object - the third thing the round-trip criterion names.</summary>
    [Serializable]
    public class NestedStats
    {
        public int Strength;
        public float Multiplier;
        public List<int> History = new List<int>();
    }

    /// <summary>Records whether the lifecycle hooks fired, and how often.</summary>
    [SaveId("hooks")]
    public class HookSaveData : SaveData
    {
        public int Value;

        [NonSerialized] public int BeforeSaveCalls;
        [NonSerialized] public int AfterLoadCalls;
        [NonSerialized] public int ResetCalls;

        protected internal override void OnBeforeSave()
        {
            BeforeSaveCalls++;
        }

        protected internal override void OnAfterLoad()
        {
            AfterLoadCalls++;
        }

        public override void ResetToDefaults()
        {
            ResetCalls++;
            Value = 0;
        }
    }

    /// <summary>Declares a schema version other than the default, to check the envelope carries it.</summary>
    [SaveId("versioned")]
    public class VersionedSaveData : SaveData
    {
        public override int SchemaVersion => 7;

        public int Value;
    }

    /// <summary>Has no [SaveId], so it exercises the snake_case fallback and its warning.</summary>
    public class UnnamedSaveData : SaveData
    {
        public int Value;
    }
}
