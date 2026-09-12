using System.Collections.Generic;
using UnityEngine;

namespace ImplicitSave.Samples.BasicUsage
{
    /// <summary>
    /// A save is a class deriving from <see cref="SaveData"/>. That is the entire setup: nothing to
    /// register, no ScriptableObject to create, no reference to drag into a field.
    /// </summary>
    /// <remarks>
    /// <c>[SaveId]</c> fixes the file name. Without it the id is derived from the class name, so
    /// renaming the class later would orphan every file already written. The <c>sample_</c> prefix
    /// only keeps this file apart from your own game's saves.
    /// <para>
    /// What gets saved follows Unity's own rules: public fields, and private fields marked
    /// <c>[SerializeField]</c>. Properties are never saved.
    /// </para>
    /// </remarks>
    [SaveId(Id)]
    public class PlayerProgressSaveData : SaveData
    {
        /// <summary>The file name.</summary>
        public const string Id = "sample_basic_progress";

        /// <summary>Currency.</summary>
        public int Coins;

        /// <summary>Player level.</summary>
        public int Level = 1;

        /// <summary>Unity structs such as <see cref="Vector3"/> are saved like any other field.</summary>
        public Vector3 LastCheckpoint;

        /// <summary>Lists work as they do in the Inspector.</summary>
        public List<string> UnlockedAreas = new List<string>();

        /// <summary>
        /// Unity cannot serialize <c>Dictionary</c>, so neither can a save. This type can, and the file
        /// shows it as a plain JSON object: <c>{"potion": 3}</c>.
        /// </summary>
        public SerializableDictionary<string, int> Inventory = new SerializableDictionary<string, int>();

        [SerializeField] private int _timesLoaded;

        /// <summary>How many times this save was read from disk. Kept in a private field.</summary>
        public int GetTimesLoaded()
        {
            return _timesLoaded;
        }

        /// <summary>Called when no file exists yet - a new game.</summary>
        public override void ResetToDefaults()
        {
            Coins = 0;
            Level = 1;
            LastCheckpoint = Vector3.zero;
            UnlockedAreas.Clear();
            Inventory.Clear();
            _timesLoaded = 0;
        }

        /// <summary>Called right after the file was read. Not called for a new game.</summary>
        protected override void OnAfterLoad()
        {
            _timesLoaded++;
        }
    }
}
