using System;
using System.Collections.Generic;
using UnityEngine;

namespace ImplicitSave.Samples.Polymorphism
{
    /// <summary>
    /// A backpack of mixed items and one equipped item, each declared as the base type. The file
    /// keeps which subtype every value was, so a Bow comes back as a Bow.
    /// </summary>
    /// <remarks>
    /// The field has to carry <c>[SerializeReference]</c> - the same rule Unity's own serializer
    /// follows. Without it Unity, and therefore this package, would store only the base type's
    /// fields and lose what made each item a sword or a bow.
    /// </remarks>
    [SaveId(Id)]
    public class LoadoutSaveData : SaveData
    {
        /// <summary>The file name. The prefix only keeps it apart from your own game's saves.</summary>
        public const string Id = "sample_poly_loadout";

        /// <summary>Items carried but not in use. Any mix of subtypes.</summary>
        [SerializeReference] public List<Item> Backpack = new List<Item>();

        /// <summary>The item in hand, or null.</summary>
        [SerializeReference] public Item Equipped;

        /// <inheritdoc />
        public override void ResetToDefaults()
        {
            Backpack.Clear();
            Equipped = null;
        }
    }

    /// <summary>The base type the save fields are declared as. Abstract is fine.</summary>
    [Serializable]
    public abstract class Item
    {
        /// <summary>Display name.</summary>
        public string Name = "";

        /// <summary>One line for the demo's list. Called through the base type.</summary>
        public abstract string Describe();
    }

    /// <summary>A melee weapon.</summary>
    /// <remarks>
    /// <c>[SaveType]</c> gives the subtype a stable id, and that id - not the class name - is what the
    /// file stores. Rename the class whenever you like. Change the id only together with
    /// <c>PreviousIds</c>, or saves written with the old id lose this value.
    /// </remarks>
    [SaveType("sample_poly_sword")]
    [Serializable]
    public class Sword : Item
    {
        /// <summary>Damage per hit.</summary>
        public int Damage;

        /// <inheritdoc />
        public override string Describe()
        {
            return Name + " - sword, " + Damage + " damage";
        }
    }

    /// <summary>A consumable.</summary>
    [SaveType("sample_poly_potion")]
    [Serializable]
    public class Potion : Item
    {
        /// <summary>Health restored per dose.</summary>
        public int Healing;

        /// <summary>Doses left.</summary>
        public int Doses = 1;

        /// <inheritdoc />
        public override string Describe()
        {
            return Name + " - potion, heals " + Healing + ", " + Doses + " dose(s)";
        }
    }

    /// <summary>A ranged weapon that can hold another item, to show polymorphism nests.</summary>
    [SaveType("sample_poly_bow")]
    [Serializable]
    public class Bow : Item
    {
        /// <summary>Range in metres.</summary>
        public int Range;

        /// <summary>Another item strapped to the bow - itself any subtype, or null.</summary>
        [SerializeReference] public Item Strapped;

        /// <inheritdoc />
        public override string Describe()
        {
            var strapped = Strapped != null ? ", with " + Strapped.Describe() + " strapped on" : "";
            return Name + " - bow, " + Range + " m" + strapped;
        }
    }
}
