using System;
using Newtonsoft.Json.Linq;

namespace ImplicitSave.Tests
{
    /// <summary>
    /// A save that has been through two schema changes, the way a real one does across two updates.
    /// </summary>
    /// <remarks>
    /// v1: <c>int Coins</c> · v2: <c>Currency.Gold</c> · v3: <c>Currency.Gold</c> plus
    /// <c>PlayerName</c>.
    /// </remarks>
    [SaveId("player_progress")]
    public class PlayerProgressSaveData : SaveData
    {
        public override int SchemaVersion => 3;

        public CurrencyData Currency = new CurrencyData();
        public string PlayerName = "";
    }

    /// <summary>The shape Coins turned into at v2.</summary>
    [Serializable]
    public class CurrencyData
    {
        public int Gold;
        public int Gems;
    }

    /// <summary>v1 to v2: the flat <c>Coins</c> field became a nested object.</summary>
    public class PlayerProgressMigration1To2 : ISaveMigration
    {
        public Type TargetType => typeof(PlayerProgressSaveData);
        public int FromVersion => 1;
        public int ToVersion => 2;

        public JObject Migrate(JObject data)
        {
            var coins = data["Coins"]?.Value<int>() ?? 0;
            data.Remove("Coins");
            data["Currency"] = new JObject { ["Gold"] = coins, ["Gems"] = 0 };
            return data;
        }
    }

    /// <summary>v2 to v3: a new field that older saves have no value for.</summary>
    public class PlayerProgressMigration2To3 : ISaveMigration
    {
        public Type TargetType => typeof(PlayerProgressSaveData);
        public int FromVersion => 2;
        public int ToVersion => 3;

        public JObject Migrate(JObject data)
        {
            data["PlayerName"] = "Aventureiro";
            return data;
        }
    }

    /// <summary>
    /// Alvo das migracoes que falham de proposito nos testes do pipeline.
    /// </summary>
    /// <remarks>
    /// Existe para que essas migracoes tenham um alvo proprio. O escaneamento do projeto encontra
    /// toda ISaveMigration, entao duas que falham apontando para um tipo real entrariam no registry
    /// e quebrariam testes que nao tem nada a ver - foi o que aconteceu na primeira versao.
    /// </remarks>
    [SaveId("migration_probe")]
    public class MigrationProbeSaveData : SaveData
    {
        public int Value;
    }

    /// <summary>
    /// Declares version 3 but ships no migrations, so an old file has nowhere to go. Used to check
    /// the error names the step that is missing.
    /// </summary>
    [SaveId("orphan_schema")]
    public class UnmigratableSaveData : SaveData
    {
        public override int SchemaVersion => 3;

        public int Value;
    }
}
