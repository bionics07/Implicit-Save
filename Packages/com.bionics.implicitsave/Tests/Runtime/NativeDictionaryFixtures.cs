using System.Collections.Generic;
using UnityEngine;

namespace ImplicitSave.Tests
{
    /// <summary>Rarity, for a dictionary keyed by an enum.</summary>
    public enum LootRarity
    {
        /// <summary>Common.</summary>
        Common,

        /// <summary>Rare.</summary>
        Rare
    }

    /// <summary>
    /// Dictionaries declared the way Unity 6.6 asks for them. Before 6.6 Unity serializes none of
    /// these, and the tests assert whichever answer the editor running them actually gives.
    /// </summary>
    [SaveId("native_dictionary_probe")]
    public class NativeDictionarySaveData : SaveData
    {
        /// <summary>String keys, the common case.</summary>
        [SerializeField] public Dictionary<string, int> Loot = new Dictionary<string, int>();

        /// <summary>Enum keys, written by name so renumbering the enum cannot silently move data.</summary>
        [SerializeField] public Dictionary<LootRarity, int> Counts = new Dictionary<LootRarity, int>();

        /// <summary>Int keys.</summary>
        [SerializeField] public Dictionary<int, string> Names = new Dictionary<int, string>();

        /// <summary>
        /// Public and without <c>[SerializeField]</c>: the one shape Unity 6.6 still skips, which
        /// makes it the case most likely to lose someone's data quietly.
        /// </summary>
        public Dictionary<string, int> NotSaved = new Dictionary<string, int>();

        /// <inheritdoc />
        public override void ResetToDefaults()
        {
            Loot.Clear();
            Counts.Clear();
            Names.Clear();
            NotSaved.Clear();
        }
    }

    /// <summary>
    /// Dictionaries a scene can hold and a save file cannot, plus the shape Unity refuses outright.
    /// Not a save type: it only feeds the rules.
    /// </summary>
    public class UnsaveableDictionaryProbe
    {
        /// <summary>Unity 6.6 serializes this; JSON has no way to write a vector as an object key.</summary>
        [SerializeField] public Dictionary<Vector3, int> ByPosition = new Dictionary<Vector3, int>();

        /// <summary>A collection inside a collection, which Unity does not serialize on any version.</summary>
        [SerializeField] public List<Dictionary<string, int>> Nested = new List<Dictionary<string, int>>();
    }
}
