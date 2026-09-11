using System;
using ImplicitSave;
using Newtonsoft.Json.Linq;
using UnityEngine;

// Seed for the CI host project. It exists so the generator tests have something to generate:
// in a project with no user save types, SaveTypeDiscovery.FindBuildableSaveTypes() comes back empty
// (test fixtures are left out because they live in test assemblies) and those tests pass without
// proving anything. Every real buyer has at least one save, so this is the honest scenario.
// Covers the three things the generated registry has to name: a save root, a subtype and a migration.

[SaveId("compat_progress")]
public class CompatProgressSaveData : SaveData
{
    public override int SchemaVersion => 2;

    public int Score;
    public string Label = "";

    [SerializeReference] public CompatReward Reward;

    public override void ResetToDefaults()
    {
        Score = 0;
        Label = "";
        Reward = null;
    }
}

[Serializable]
public abstract class CompatReward
{
    public int Amount;
}

[SaveType("compat_coin_reward")]
[Serializable]
public class CompatCoinReward : CompatReward
{
    public string Currency = "gold";
}

public class CompatProgressMigration1To2 : ISaveMigration
{
    public Type TargetType => typeof(CompatProgressSaveData);
    public int FromVersion => 1;
    public int ToVersion => 2;

    public JObject Migrate(JObject data)
    {
        return data;
    }
}
