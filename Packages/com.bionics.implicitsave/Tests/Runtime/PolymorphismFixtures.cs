using System;
using System.Collections.Generic;
using UnityEngine;

namespace ImplicitSave.Tests
{
    /// <summary>
    /// The base of a polymorphic branch. Abstract on purpose: Unity cannot serialize this in an
    /// ordinary field, which is the whole reason <c>[SerializeReference]</c> exists.
    /// </summary>
    [Serializable]
    public abstract class Weapon
    {
        public string Name = "";
        public int Damage;
    }

    /// <summary>The id is what reaches the file, so renaming this class cannot break a save.</summary>
    [SaveType("melee_weapon")]
    [Serializable]
    public class MeleeWeapon : Weapon
    {
        public float Reach = 1.5f;
    }

    [SaveType("bow")]
    [Serializable]
    public class Bow : Weapon
    {
        public int ArrowCount = 12;
        public float DrawTime = 0.8f;

        /// <summary>A polymorphic field inside a polymorphic value, so nesting is covered too.</summary>
        [SerializeReference] public Weapon Sidearm;
    }

    /// <summary>A third subtype, to prove the picker lists more than two.</summary>
    [SaveType("magic_staff")]
    [Serializable]
    public class MagicStaff : Weapon
    {
        public int Mana = 50;
        public string Element = "fire";
    }

    /// <summary>
    /// A save holding polymorphic values: one field and one list, which are the two shapes the
    /// converter has to handle.
    /// </summary>
    [SaveId("loadout")]
    public class LoadoutSaveData : SaveData
    {
        [SerializeReference] public Weapon Equipped;
        [SerializeReference] public List<Weapon> Backpack = new List<Weapon>();

        public string OwnerName = "";

        public override void ResetToDefaults()
        {
            Equipped = null;
            Backpack = new List<Weapon>();
            OwnerName = "";
        }
    }
}
