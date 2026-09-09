using System;
using System.Collections.Generic;
using ImplicitSave;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// O save de demonstração. Tem um campo de cada coisa que o package precisa aguentar, para a janela
/// de editor e a scene de teste exercitarem tudo de uma vez.
/// </summary>
[SaveId("demo")]
public class DemoChangeNameSaveData : SaveData
{
    /// <summary>v1 era só `Coins`; v2 trocou por `Currency`. Ver <see cref="DemoMigration1To2"/>.</summary>
    public override int SchemaVersion => 2;

    [Header("Primitivos")]
    public string PlayerName = "Sem nome";
    public int Level = 1;
    public float Health = 100f;
    public bool TutorialDone;

    [Header("Enum e objeto aninhado")]
    public Difficulty Difficulty = Difficulty.Normal;
    public CurrencyPurse Currency = new CurrencyPurse();

    [Header("Listas")]
    public List<string> UnlockedLevels = new List<string>();
    public List<Vector3> Checkpoints = new List<Vector3>();

    [Header("Dicionarios — o que o Inspector normal nao desenha")]
    public SerializableDictionary<string, int> Inventory = new SerializableDictionary<string, int>();
    public SerializableDictionary<int, string> SlotNames = new SerializableDictionary<int, string>();
    public SerializableDictionary<Difficulty, int> BestScoreByDifficulty = new SerializableDictionary<Difficulty, int>();

    [Header("Campo privado com [SerializeField]")]
    [SerializeField] private int _secretCounter;

    [Header("Property drawer customizado")]
    public HealthBar Bar = new HealthBar();

    [Header("Polimorfismo — [SerializeReference] (fase 8)")]

    /// <summary>
    /// Campo polimórfico simples. O tipo declarado é abstrato: sem <c>[SerializeReference]</c> a
    /// Unity ignoraria o campo inteiro, e o arquivo também.
    /// </summary>
    [SerializeReference] public Ability Equipped;

    /// <summary>
    /// Lista polimórfica. Cada entrada pode ser de um tipo diferente, e o picker aparece uma vez por
    /// entrada.
    /// </summary>
    [SerializeReference] public List<Ability> Loadout = new List<Ability>();

    /// <summary>Property: nunca é salva. Existe para o validador reclamar dela.</summary>
    public int DoubledLevel => Level * 2;

    [NonSerialized] public int RuntimeOnlyValue;

    public int ReadSecretCounter() => _secretCounter;

    public void BumpSecretCounter() => _secretCounter++;

    public override void ResetToDefaults()
    {
        PlayerName = "Sem nome";
        Level = 1;
        Health = 100f;
        TutorialDone = false;
        Difficulty = Difficulty.Normal;
        Currency = new CurrencyPurse();
        UnlockedLevels = new List<string>();
        Checkpoints = new List<Vector3>();
        Inventory = new SerializableDictionary<string, int>();
        SlotNames = new SerializableDictionary<int, string>();
        BestScoreByDifficulty = new SerializableDictionary<Difficulty, int>();
        _secretCounter = 0;
        Bar = new HealthBar();
        Equipped = null;
        Loadout = new List<Ability>();
    }
}

/// <summary>
/// A base das habilidades. Abstrata de propósito: guardar "alguma habilidade" só é possível por
/// referência, e é esse o caso que a fase 8 resolve.
/// </summary>
/// <remarks>
/// O <see cref="Describe"/> não é exigido pelo package — está aqui porque deixa o playground provar
/// que o polimorfismo funcionou de verdade: ele chama pela base e quem responde é o subtipo que
/// voltou do disco.
/// </remarks>
[Serializable]
public abstract class Ability
{
    public string Name = "";

    [Range(0f, 30f)] public float Cooldown = 1f;

    public abstract string Describe();
}

/// <summary>
/// O id é o que vai para o arquivo, dentro de <c>$t</c>. Renomear esta classe não quebra save
/// nenhum; mudar este texto quebra todos.
/// </summary>
[SaveType("ability_meteor")]
[Serializable]
public class MetorAbility : Ability
{
    public int Power = 30;
    public float Radius = 2.5f;

    public override string Describe() => $"bola de fogo {Power} de dano, raio {Radius}";
}

[SaveType("ability_heal")]
[Serializable]
public class HealAbility : Ability
{
    public int Amount = 25;
    public bool CuresPoison;

    public override string Describe() => $"cura {Amount}" + (CuresPoison ? " e limpa veneno" : "");
}

/// <summary>
/// Guarda outra habilidade dentro de si — inclusive outra combo. É o caso aninhado: o picker tem
/// que aparecer também aqui dentro, e o <c>$t</c> tem que sair em todos os níveis.
/// </summary>
[SaveType("ability_combo")]
[Serializable]
public class ComboAbility : Ability
{
    [SerializeReference] public Ability Then;

    public override string Describe()
    {
        return "combo → " + (Then == null ? "(nada encadeado)" : Then.Describe());
    }
}

/// <summary>Chaves de enum são gravadas por nome, então renumerar não remapeia nada.</summary>
public enum Difficulty
{
    Easy = 0,
    Normal = 10,
    Hard = 20
}

/// <summary>Objeto aninhado. Precisa de [Serializable] — a classe de save não precisa.</summary>
[Serializable]
public class CurrencyPurse
{
    public int Gold;
    public int Gems;
}

/// <summary>Existe só para provar que o property drawer do usuário é respeitado na janela.</summary>
[Serializable]
public class HealthBar
{
    [Range(0f, 1f)] public float Fill = 1f;
    public Color Tint = Color.green;
}

/// <summary>
/// v1 → v2: o campo plano `Coins` virou o objeto `Currency`. É a migração que a scene de teste
/// dispara no botão "criar save v1".
/// </summary>
public class DemoMigration1To2 : ISaveMigration
{
    public Type TargetType => typeof(DemoChangeNameSaveData);
    public int FromVersion => 1;
    public int ToVersion => 2;

    /// <remarks>
    /// Duas regras que valem para qualquer migração que você escrever:
    /// <list type="number">
    /// <item><b>Mexa só no que a migração conhece.</b> Aqui, isso é exatamente um campo: `Coins`.
    /// Sem `Coins` no arquivo, não há nada a fazer e a migração devolve o payload intacto.</item>
    /// <item><b>Não escreva defaults.</b> Um campo ausente deve ficar a cargo da desserialização,
    /// que aplica o inicializador da classe. Escrever `Gems = 0` aqui atropelaria um eventual
    /// `public int Gems = 10;` e o save migrado nasceria errado — com o bug parecendo vir da classe,
    /// não daqui.</item>
    /// </list>
    /// O que não existia na v1 (Gems, por exemplo) simplesmente não tem como ser preservado. Chutar
    /// um valor seria pior que deixar no default: dá ao jogador moeda que ele nunca teve, e ninguém
    /// consegue distinguir isso depois.
    /// </remarks>
    public JObject Migrate(JObject data)
    {
        if (data["Coins"] == null)
        {
            return data;
        }

        // Preserva o Currency que porventura já exista em vez de substituir o objeto inteiro.
        var currency = data["Currency"] as JObject ?? new JObject();
        currency["Gold"] = data["Coins"].Value<int>();
        data.Remove("Coins");

        data["Currency"] = currency;
        return data;
    }
}
