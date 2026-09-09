using System;
using ImplicitSave;
using UnityEngine;

/// <summary>
/// A base de um valor polimórfico dentro do save órfão. Só ela é nomeada por código; o subtipo
/// concreto, não.
/// </summary>
/// <remarks>
/// O método abstrato existe para que o harness consiga provar que o subtipo voltou sem escrever o
/// nome dele em lugar nenhum — chamar <c>Describe()</c> pela base é o que torna o teste válido.
/// </remarks>
[Serializable]
public abstract class OrphanBadge
{
    public string Label = "";

    public abstract string Describe();
}

/// <summary>
/// O subtipo do teste. Assim como <see cref="OrphanSaveData"/>, nenhuma linha de código o
/// referencia: ele só existe no arquivo como o id <c>"orphan_gold_badge"</c> em <c>$t</c>, e só
/// chega à memória porque o registry gerado sabe construí-lo.
/// </summary>
/// <remarks>
/// É o caso mais duro do stripping. O tipo do save ao menos aparece no <c>link.xml</c> por ser um
/// <c>SaveData</c>; um subtipo polimórfico não tem nem isso a não ser que o gerador o inclua.
/// </remarks>
[SaveType("orphan_gold_badge")]
[Serializable]
public class OrphanGoldBadge : OrphanBadge
{
    public int Karats = 24;

    public override string Describe()
    {
        return "ouro " + Karats + "k";
    }
}

/// <summary>
/// O tipo de save do teste de stripping. Ele existe, é declarado, e **nenhuma linha de código em
/// lugar nenhum o referencia** — nem por `new`, nem por `typeof`, nem como parâmetro genérico.
/// </summary>
/// <remarks>
/// Isso é o ponto do teste. Para o linker do IL2CPP, uma classe que ninguém referencia é código
/// morto e vai embora do build. Se este tipo sobreviver, o registry gerado está fazendo o trabalho
/// dele. Se sumir, o package quebra em produção e funciona no editor.
/// <para>
/// Se você algum dia escrever <c>OrphanSaveData</c> em qualquer script, o teste perde a validade —
/// essa referência sozinha já salvaria a classe do linker.
/// </para>
/// </remarks>
[SaveId("orphan")]
public class OrphanSaveData : SaveData
{
    /// <summary>Incrementado a cada toque no botão. É o valor que precisa sobreviver ao restart.</summary>
    public int Counter;

    /// <summary>Escrito junto, para provar que mais de um campo sobreviveu ao stripping.</summary>
    public string LastNote = "";

    /// <summary>
    /// Quantas vezes o app abriu. Torna o print autoexplicativo: sem isso não dá para distinguir
    /// "o valor voltou do disco depois de matar o app" de "o valor ainda está na memória".
    /// </summary>
    public int BootCount;

    /// <summary>
    /// O valor polimórfico. Sob stripping é o campo mais frágil do teste: o que o arquivo guarda é
    /// só o id em <c>$t</c>, então se o subtipo tiver sido removido do build isto volta como erro,
    /// não como <c>null</c> silencioso.
    /// </summary>
    [SerializeReference] public OrphanBadge Badge;

    public override void ResetToDefaults()
    {
        Counter = 0;
        LastNote = "";
        BootCount = 0;
        Badge = null;
    }
}
