using System;
using System.Text;
using ImplicitSave;
using UnityEngine;

/// <summary>
/// Cenário do critério obrigatório da §6: prova, num build IL2CPP com stripping alto, que um
/// <c>SaveData</c> que ninguém referencia continua existindo, salvando e persistindo.
/// </summary>
/// <remarks>
/// Regra que faz o teste valer: este script **nunca** nomeia o tipo de save. Tudo aqui passa por
/// string — o id "orphan" — e pelo registry. Uma única menção a <c>OrphanSaveData</c> no código já
/// criaria a referência que salva a classe do linker, e o teste passaria por engano.
/// <para>
/// Os campos são lidos e escritos por reflection de propósito: assim o teste cobre também o
/// <c>link.xml</c>, que é quem preserva os campos e o construtor sob stripping agressivo.
/// </para>
/// </remarks>
public class StrippingTestHarness : MonoBehaviour
{
    private const string TargetSaveId = "orphan";

    /// <summary>
    /// O id do subtipo. Como o do save, ele só aparece aqui como texto: quem transforma isso num
    /// objeto é o registry gerado.
    /// </summary>
    private const string BadgeTypeId = "orphan_gold_badge";

    private readonly StringBuilder _report = new StringBuilder();
    private GUIStyle _style;
    private string _status = "";
    private bool _passed;

    private void Start()
    {
        Application.targetFrameRate = 30;
        CountThisBoot();
        Inspect();
    }

    /// <summary>
    /// Conta esta abertura do app e grava. É o que torna o print sozinho suficiente: se o número
    /// subiu, o processo morreu de verdade entre uma leitura e outra.
    /// </summary>
    private void CountThisBoot()
    {
        try
        {
            if (!SaveTypeRegistry.TryGetType(TargetSaveId, out var type))
            {
                return;
            }

            var profileId = SaveManager.ActiveProfileId;
            var data = SaveManager.Get(type, profileId);
            WriteField(data, "BootCount", (int)ReadField(data, "BootCount") + 1);

            // Na primeira abertura, cria o valor polimórfico. Repare que o subtipo nasce de uma
            // STRING: SaveTypeRegistry.Create é a única porta, e é exatamente a porta que o
            // stripping fecharia.
            if (ReadField(data, "Badge") == null)
            {
                WriteField(data, "Badge", SaveTypeRegistry.Create(BadgeTypeId));
            }

            SaveManager.Save(type, profileId);
        }
        catch (Exception e)
        {
            _status = "erro ao contar boot: " + e.Message;
        }
    }

    /// <summary>Monta o relatório que aparece na tela do device.</summary>
    private void Inspect()
    {
        _report.Length = 0;
        _passed = false;

        _report.AppendLine("ImplicitSave — teste de stripping");
        _report.AppendLine("plataforma: " + Application.platform);
        _report.AppendLine("tipos no registry: " + SaveTypeRegistry.Count);
        _report.AppendLine();

        if (SaveTypeRegistry.Count == 0)
        {
            _report.AppendLine("FALHOU: o registry está vazio.");
            _report.AppendLine("O SaveRegistry.g.cs não entrou no build.");
            return;
        }

        if (!SaveTypeRegistry.TryGetType(TargetSaveId, out var type))
        {
            _report.AppendLine("FALHOU: o id '" + TargetSaveId + "' não está no registry.");
            _report.AppendLine("O tipo foi removido pelo stripping.");
            return;
        }

        _report.AppendLine("tipo encontrado: " + type.FullName);
        _report.AppendLine("assembly: " + type.Assembly.GetName().Name);

        try
        {
            var data = SaveManager.Get(type, SaveManager.ActiveProfileId);
            var counter = ReadField(data, "Counter");
            var note = ReadField(data, "LastNote");

            _report.AppendLine();
            _report.AppendLine("aberturas do app: " + ReadField(data, "BootCount"));
            _report.AppendLine("Counter: " + counter);
            _report.AppendLine("LastNote: " + (string.IsNullOrEmpty(note as string) ? "(vazio)" : note));

            // Chamado pela base, nunca pelo subtipo. Se isto imprimir "ouro 24k", o subtipo
            // sobreviveu ao linker e voltou do disco só pelo $t.
            var badge = ReadField(data, "Badge") as OrphanBadge;
            _report.AppendLine("Badge: " + (badge == null
                ? "(nenhum) — o subtipo pode ter sido removido"
                : badge.Describe() + "  [" + badge.GetType().Name + "]"));

            _report.AppendLine();
            _report.AppendLine("O tipo carregou. Toque em SALVAR, mate o app pelo");
            _report.AppendLine("switcher e abra de novo: o Counter tem que continuar.");
            _passed = true;
        }
        catch (Exception e)
        {
            _report.AppendLine();
            _report.AppendLine("FALHOU ao ler os campos: " + e.GetType().Name);
            _report.AppendLine(e.Message);
            _report.AppendLine("Provavelmente o link.xml não preservou os campos.");
        }
    }

    private void Increment()
    {
        try
        {
            if (!SaveTypeRegistry.TryGetType(TargetSaveId, out var type))
            {
                _status = "sem o tipo, nada a salvar";
                return;
            }

            var profileId = SaveManager.ActiveProfileId;
            var data = SaveManager.Get(type, profileId);

            WriteField(data, "Counter", (int)ReadField(data, "Counter") + 1);
            WriteField(data, "LastNote", "salvo em " + DateTime.Now.ToString("HH:mm:ss"));

            SaveManager.Save(type, profileId);

            _status = "salvo";
            Inspect();
        }
        catch (Exception e)
        {
            _status = "erro ao salvar: " + e.Message;
        }
    }

    /// <summary>
    /// Muda o valor só na memória, sem chamar <c>Save</c>. É o cenário do hook de pause: quem tem
    /// que gravar isso é o <c>OnApplicationPause</c>, quando você minimizar o app.
    /// </summary>
    private void ChangeWithoutSaving()
    {
        try
        {
            if (!SaveTypeRegistry.TryGetType(TargetSaveId, out var type))
            {
                _status = "sem o tipo";
                return;
            }

            var data = SaveManager.Get(type, SaveManager.ActiveProfileId);
            WriteField(data, "Counter", (int)ReadField(data, "Counter") + 100);
            WriteField(data, "LastNote", "alterado sem salvar as " + DateTime.Now.ToString("HH:mm:ss"));

            _status = "memoria alterada, NADA salvo. Minimize o app agora.";
            Inspect();
        }
        catch (Exception e)
        {
            _status = "erro: " + e.Message;
        }
    }

    // Reflection de propósito: nomear o tipo aqui invalidaria o teste inteiro.
    private static object ReadField(object instance, string fieldName)
    {
        var field = instance.GetType().GetField(fieldName);
        if (field == null)
        {
            throw new MissingFieldException(instance.GetType().Name, fieldName);
        }

        return field.GetValue(instance);
    }

    private static void WriteField(object instance, string fieldName, object value)
    {
        var field = instance.GetType().GetField(fieldName);
        if (field == null)
        {
            throw new MissingFieldException(instance.GetType().Name, fieldName);
        }

        field.SetValue(instance, value);
    }

    private void OnGUI()
    {
        if (_style == null)
        {
            _style = new GUIStyle(GUI.skin.label)
            {
                fontSize = Mathf.Max(18, Screen.height / 34),
                wordWrap = true
            };
        }

        var margin = Screen.width * 0.05f;
        var width = Screen.width - (margin * 2);

        GUI.color = _passed ? Color.white : new Color(1f, 0.5f, 0.5f);
        GUI.Label(new Rect(margin, margin, width, Screen.height * 0.6f), _report.ToString(), _style);
        GUI.color = Color.white;

        var buttonHeight = Mathf.Max(60, Screen.height * 0.09f);
        var y = Screen.height - margin - (buttonHeight * 2) - 20;

        if (GUI.Button(new Rect(margin, y, width, buttonHeight), "SALVAR (+1)"))
        {
            Increment();
        }

        if (GUI.Button(new Rect(margin, y + buttonHeight + 20, width, buttonHeight), "ALTERAR SEM SALVAR"))
        {
            ChangeWithoutSaving();
        }

        if (!string.IsNullOrEmpty(_status))
        {
            GUI.Label(new Rect(margin, y - 40, width, 40), _status, _style);
        }
    }
}
