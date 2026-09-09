using System;
using UnityEngine;

namespace ImplicitSave.Tests
{
    /// <summary>
    /// A base whose subtypes are the point: one carries an id and one does not, which is what the
    /// validator has to tell apart.
    /// </summary>
    [Serializable]
    public abstract class Probe
    {
        public string Label = "";
    }

    [SaveType("probe_tagged")]
    [Serializable]
    public class ProbeTagged : Probe
    {
    }

    /// <summary>
    /// Deliberately missing its <c>[SaveType]</c>. Saving one of these would fail, and the validator
    /// exists to say so before a player ever gets there.
    /// </summary>
    [Serializable]
    public class ProbeUntagged : Probe
    {
    }

    /// <summary>
    /// Holds the broken subtype through a by-reference field, which is the only way the validator
    /// can reach it.
    /// </summary>
    [SaveId("probe_loadout")]
    public class ProbeLoadoutSaveData : SaveData
    {
        [SerializeReference] public Probe Equipped;

        public override void ResetToDefaults()
        {
            Equipped = null;
        }
    }
}
