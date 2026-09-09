using System;
using System.Collections.Generic;
using System.Text;
using ImplicitSave;
using ImplicitSave.Storage;
using UnityEngine;

/// <summary>
/// Painel de teste manual do ImplicitSave. Cada botão exercita um comportamento específico de uma
/// das fases, para dar pra ver o package funcionando sem escrever código.
/// </summary>
/// <remarks>
/// Rode em play mode e deixe a janela <c>Tools &gt; ImplicitSave &gt; Save Editor</c> aberta ao lado:
/// as duas mexem na mesma instância viva, que é o critério mais delicado da Fase 7.
/// </remarks>
public class SavePlayground : MonoBehaviour
{
    private readonly List<string> _log = new List<string>();
    private Vector2 _scroll;
    private Vector2 _panelScroll;
    private GUIStyle _logStyle;

    private void Start()
    {
        Say("Playground pronto. Profile ativo: " + SaveManager.ActiveProfileId);
        Say("Tipos no registry: " + SaveTypeRegistry.Count + "  ·  migrações: " +
            SaveTypeRegistry.GetMigrations().Count);

        SaveManager.Failed += e => Say("FALHA (" + e.GetType().Name + "): " + e.Message);
        SaveManager.Saved += (type, profile) => Say("evento Saved: " + type.Name + " no profile " + profile);
        SaveManager.Loaded += (type, profile) => Say("evento Loaded: " + type.Name + " do profile " + profile);
    }

    private void OnGUI()
    {
        _logStyle = _logStyle ?? new GUIStyle(GUI.skin.label) { wordWrap = true, fontSize = 11 };

        using (new GUILayout.AreaScope(new Rect(10, 10, 380, Screen.height - 20), GUIContent.none, GUI.skin.box))
        {
            GUILayout.Label("ImplicitSave — playground", EditorLikeHeader());
            GUILayout.Label("Profile ativo: " + SaveManager.ActiveProfileId, _logStyle);
            GUILayout.Space(4);

            // Com scroll: a lista de botões passa da altura da tela em resolução de janela normal,
            // e sem isto os últimos ficam simplesmente fora de vista, sem nenhum sinal disso.
            _panelScroll = GUILayout.BeginScrollView(_panelScroll);

            DrawFase1e2();
            DrawFase3();
            DrawFase5();
            DrawFase6();
            DrawFase8();
            DrawInspecao();

            GUILayout.EndScrollView();
        }

        DrawLog();
    }

    private void DrawFase1e2()
    {
        GUI.color = new Color(1f, 0.75f, 0.75f);
        if (GUILayout.Button("APAGAR TUDO e recomeçar do zero"))
        {
            ResetEverything();
        }
        GUI.color = Color.white;

        GUILayout.Space(4);
        GUILayout.Label("— dados e dicionários (fases 1 e 2)", EditorLikeHeader());

        if (GUILayout.Button("Preencher o save com dados variados"))
        {
            var data = SaveManager.Get<DemoChangeNameSaveData>();
            data.PlayerName = "Ana";
            data.Level = UnityEngine.Random.Range(2, 40);
            data.Health = UnityEngine.Random.Range(10f, 100f);
            data.TutorialDone = true;
            data.Difficulty = Difficulty.Hard;
            data.Currency.Gold = UnityEngine.Random.Range(100, 9999);
            data.Currency.Gems = 42;
            data.UnlockedLevels.Add("floresta_" + data.UnlockedLevels.Count);
            data.Checkpoints.Add(new Vector3(1, 2, 3));
            data.Inventory["pocao"] = UnityEngine.Random.Range(1, 20);
            data.Inventory["corda"] = 2;
            data.SlotNames[1] = "primeiro";
            data.SlotNames[1000] = "milesimo";
            data.BestScoreByDifficulty[Difficulty.Hard] = 9001;
            data.BumpSecretCounter();
                data.Bar.Fill = UnityEngine.Random.value;
            data.Equipped = RandomAbility();
            Say("Preenchido em memória. Nada foi gravado ainda.");
        }

        if (GUILayout.Button("Salvar agora"))
        {
            SaveManager.Save<DemoChangeNameSaveData>();
            Say("Salvo.");
        }

        if (GUILayout.Button("Descartar memória e reler do disco"))
        {
            SaveManager.Reload(SaveManager.ActiveProfileId);
            var data = SaveManager.Get<DemoChangeNameSaveData>();
            Say("Relido: " + data.PlayerName + " nvl " + data.Level + ", ouro " + data.Currency.Gold +
                ", inventário com " + data.Inventory.Count + " itens, contador privado " +
                data.ReadSecretCounter());
        }
    }

    private void DrawFase3()
    {
        GUILayout.Label("— profiles (fase 3)", EditorLikeHeader());

        using (new GUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Criar slot"))
            {
                var profile = SaveManager.CreateProfile();
                Say("Criado o slot " + profile.Id + " (" + profile.DisplayName + ")");
            }

            if (GUILayout.Button("Trocar de slot"))
            {
                var profiles = SaveManager.GetProfiles();
                var next = profiles[(IndexOfActive(profiles) + 1) % profiles.Count].Id;
                SaveManager.SetActiveProfile(next);
                Say("Agora no slot " + next + ". O anterior foi gravado antes da troca.");
            }
        }

        if (GUILayout.Button("Listar slots"))
        {
            foreach (var profile in SaveManager.GetProfiles())
            {
                Say("  slot " + profile.Id + ": " + profile.DisplayName + " · " +
                    Mathf.RoundToInt((float)profile.PlayTimeSeconds) + "s jogados");
            }
        }
    }

    private void DrawFase5()
    {
        GUILayout.Label("— autosave e dirty tracking (fase 5)", EditorLikeHeader());

        if (GUILayout.Button("Tick de autosave SEM mudar nada"))
        {
            var before = CountWrites();
            SaveManager.AutoSave();
            SaveManager.FlushSynchronously(TimeSpan.FromSeconds(5));
            Say(CountWrites() == before
                ? "Nada foi escrito — nada tinha mudado. É o dirty tracking funcionando."
                : "Escreveu (esperado só se algo mudou antes).");
        }

        if (GUILayout.Button("Mudar DENTRO do dicionário e dar tick"))
        {
            var data = SaveManager.Get<DemoChangeNameSaveData>();
            data.Inventory["pocao"] = data.Inventory.TryGetValue("pocao", out var q) ? q + 1 : 1;
            Say("Mudei Inventory[\"pocao\"] sem chamar MarkDirty.");

            var before = CountWrites();
            SaveManager.AutoSave();
            SaveManager.FlushSynchronously(TimeSpan.FromSeconds(5));
            Say(CountWrites() > before
                ? "Escreveu. O hash pegou a mudança dentro da coleção."
                : "NÃO escreveu — isso seria um bug.");
        }
    }

    private void DrawFase6()
    {
        GUILayout.Label("— migração (fase 6)", EditorLikeHeader());

        if (GUILayout.Button("1. Substituir o arquivo por um save v1 (formato antigo)"))
        {
            var json = WriteRaw(1, "{\"Coins\": 1250, \"PlayerName\": \"Jogador antigo\", \"Level\": 7}");
            SaveManager.Reload(SaveManager.ActiveProfileId);

            Say("SUBSTITUI o arquivo inteiro — é o save de um jogador que não atualiza há 2 versões.");
            Say("O que você tinha antes foi embora junto. Isso é do teste, não da migração.");
            Say("ATENÇÃO às Gems: elas nasceram na v2, então um arquivo v1 NÃO TEM esse campo.");
            Say("Depois de migrar elas vêm zeradas — não é perda, é ausência de dado no arquivo antigo.");
            Say("Repare no campo 'Coins', que não existe mais na classe:");
            Say(json);
            Say("Agora abra o Save Editor: 'demo' aparece com ⚠ de migração pendente.");
        }

        if (GUILayout.Button("1b. Substituir por um v1 que JÁ tem Gems (prova a preservação)"))
        {
            var json = WriteRaw(1, "{\"Coins\": 99, \"Currency\": {\"Gems\": 42}, \"PlayerName\": \"Com gems\"}");
            SaveManager.Reload(SaveManager.ActiveProfileId);

            Say("Arquivo v1 fora do comum: tem Coins E um Currency com 42 gems.");
            Say("Uma migração descuidada sobrescreveria Currency inteiro e zeraria as gems.");
            Say("Carregue no passo 2: Gold vira 99 e as gems têm que continuar 42.");
            Say(json);
        }

        if (GUILayout.Button("2. Carregar (roda a migração v1 → v2)"))
        {
            var data = SaveManager.Get<DemoChangeNameSaveData>();
            Say("Carregado: Coins virou Currency.Gold = " + data.Currency.Gold +
                " · Gems = " + data.Currency.Gems + " · nome '" + data.PlayerName + "'");

            if (data.Currency.Gems == 0)
            {
                Say("Gems = 0 porque o arquivo v1 não tinha esse campo. Use o passo 1b para ver a " +
                    "preservação funcionando.");
            }
            Say("A janela nunca mostra 'Coins' — ela desenha a CLASSE, e a classe não tem esse campo.");
            Say("Para ver o arquivo cru use 'Mostrar o JSON do arquivo'.");
        }

        if (GUILayout.Button("3. Salvar (grava de volta já na v2)"))
        {
            SaveManager.Save<DemoChangeNameSaveData>();
            Say("Salvo. O arquivo agora é v2 e não precisa mais migrar:");
            Say(ReadRaw());
        }

        if (GUILayout.Button("Escrever um save v99 (versão futura)"))
        {
            WriteRaw(99, "{\"PlayerName\": \"De um build mais novo\", \"CampoDoFuturo\": 123}");
            SaveManager.Reload(SaveManager.ActiveProfileId);
            var data = SaveManager.Get<DemoChangeNameSaveData>();
            Say("IsReadOnly = " + data.IsReadOnly + ". O arquivo não vai ser sobrescrito.");
        }

        if (GUILayout.Button("Tentar salvar por cima do save futuro"))
        {
            var before = ReadRaw();
            SaveManager.Get<DemoChangeNameSaveData>().Level = 999;
            SaveManager.ForceSave();
            Say(before == ReadRaw()
                ? "Arquivo intacto, byte a byte. Nem com ForceSave ele é destruído."
                : "O ARQUIVO MUDOU — isso seria um bug grave.");
        }

        if (GUILayout.Button("Corromper o arquivo (testa o .bak)"))
        {
            var storage = new FileSaveStorage(ImplicitSaveSettings.Instance.SaveFolderName);
            System.IO.File.WriteAllText(storage.GetSavePath(SaveManager.ActiveProfileId, "demo"), "{ truncado");
            SaveManager.Reload(SaveManager.ActiveProfileId);
            var data = SaveManager.Get<DemoChangeNameSaveData>();
            Say("Recuperado: nome '" + data.PlayerName + "'. Veio do backup, ou é instância nova se " +
                "o backup também falhou.");
        }
    }

    /// <summary>
    /// Polimorfismo. Tudo aqui mexe só na memória — salve e abra a Save Editor Window para ver o
    /// picker, ou salve e releia para ver o que sobreviveu ao arquivo.
    /// </summary>
    private void DrawFase8()
    {
        GUILayout.Space(4);
        GUILayout.Label("— habilidades polimórficas (fase 8)", EditorLikeHeader());

        if (GUILayout.Button("Equipar uma habilidade sorteada"))
        {
            var data = SaveManager.Get<DemoChangeNameSaveData>();
            data.Equipped = RandomAbility();
            Say("Equipado: " + data.Equipped.Describe());
        }

        if (GUILayout.Button("Somar uma habilidade ao loadout"))
        {
            var data = SaveManager.Get<DemoChangeNameSaveData>();
            data.Loadout.Add(RandomAbility());
            Say("Loadout agora tem " + data.Loadout.Count + " habilidade(s). Tipos podem ser diferentes " +
                "entre si — é esse o ponto.");
        }

        if (GUILayout.Button("Equipar uma combo aninhada"))
        {
            var data = SaveManager.Get<DemoChangeNameSaveData>();
            data.Equipped = new ComboAbility
            {
                Name = "Sequência",
                Then = new ComboAbility
                {
                    Name = "Segunda parte",
                    Then = new MetorAbility { Name = "Estouro", Power = 60 }
                }
            };

            Say("Equipado: " + data.Equipped.Describe() + "\nNa janela, o picker aparece em cada nível.");
        }

        // Este botão existe para PRODUZIR o problema, não para escondê-lo.
        if (GUILayout.Button("Pôr a MESMA instância em dois lugares (gera o aviso)"))
        {
            var data = SaveManager.Get<DemoChangeNameSaveData>();
            var shared = RandomAbility();
            shared.Name = "Compartilhada";

            data.Equipped = shared;
            data.Loadout.Add(shared);

            Say("O mesmo objeto está em Equipped e no Loadout. Salve e abra a janela: ela avisa que o " +
                "arquivo não sabe representar isso.\nSalve e releia: viram DOIS objetos diferentes.");
        }

        if (GUILayout.Button("Listar o que está equipado"))
        {
            var data = SaveManager.Get<DemoChangeNameSaveData>();
            var text = "Equipped: " + (data.Equipped == null ? "(vazio)" : Show(data.Equipped));

            for (var i = 0; i < data.Loadout.Count; i++)
            {
                text += "\nLoadout[" + i + "]: " + (data.Loadout[i] == null ? "(vazio)" : Show(data.Loadout[i]));
            }

            if (data.Equipped != null && data.Loadout.Contains(data.Equipped))
            {
                text += "\n\nAtenção: Equipped e uma entrada do loadout são o MESMO objeto.";
            }

            Say(text);
        }

        if (GUILayout.Button("Limpar habilidades"))
        {
            var data = SaveManager.Get<DemoChangeNameSaveData>();
            data.Equipped = null;
            data.Loadout.Clear();
            Say("Habilidades limpas em memória.");
        }
    }

    /// <summary>
    /// Descreve chamando pela base. Se isto imprimir a descrição certa depois de reler do disco, o
    /// <c>$t</c> fez o trabalho dele.
    /// </summary>
    private static string Show(Ability ability)
    {
        return ability.Describe() + "   [" + ability.GetType().Name + "]";
    }

    private static Ability RandomAbility()
    {
        switch (UnityEngine.Random.Range(0, 3))
        {
            case 0:
                return new MetorAbility { Name = "Bola de fogo", Power = UnityEngine.Random.Range(10, 80) };
            case 1:
                return new HealAbility { Name = "Cura", Amount = UnityEngine.Random.Range(5, 60), CuresPoison = true };
            default:
                return new ComboAbility { Name = "Combo", Then = new HealAbility { Name = "Cura encadeada" } };
        }
    }

    private void DrawInspecao()
    {
        GUILayout.Label("— inspeção", EditorLikeHeader());

        if (GUILayout.Button("Mostrar o JSON do arquivo"))
        {
            var raw = ReadRaw();
            Say(raw ?? "(ainda não existe arquivo)");
        }

        if (GUILayout.Button("Apagar o arquivo do save"))
        {
            var storage = new FileSaveStorage(ImplicitSaveSettings.Instance.SaveFolderName);
            storage.Delete(SaveManager.ActiveProfileId, "demo");
            SaveManager.Reload(SaveManager.ActiveProfileId);
            Say("Apagado. No Save Editor ele volta a aparecer com ○ de 'sem arquivo'.");
        }

        if (GUILayout.Button("Limpar log"))
        {
            _log.Clear();
        }

    }

    private void DrawLog()
    {
        using (new GUILayout.AreaScope(
                   new Rect(400, 10, Screen.width - 410, Screen.height - 20), GUIContent.none, GUI.skin.box))
        {
            GUILayout.Label("log", EditorLikeHeader());
            _scroll = GUILayout.BeginScrollView(_scroll);

            for (var i = _log.Count - 1; i >= 0; i--)
            {
                GUILayout.Label(_log[i], _logStyle);
            }

            GUILayout.EndScrollView();
        }
    }

    private static int IndexOfActive(IReadOnlyList<ProfileInfo> profiles)
    {
        for (var i = 0; i < profiles.Count; i++)
        {
            if (profiles[i].Id == SaveManager.ActiveProfileId)
            {
                return i;
            }
        }

        return 0;
    }

    /// <summary>Escreve um arquivo numa versão de schema escolhida, sem passar pelo SaveManager.</summary>
    /// <returns>O JSON gravado, para dar pra ver o formato antigo na tela.</returns>
    private static string WriteRaw(int schemaVersion, string payload)
    {
        var storage = new FileSaveStorage(ImplicitSaveSettings.Instance.SaveFolderName);
        var json = "{\"$saveId\":\"demo\",\"$schemaVersion\":" + schemaVersion +
                   ",\"$savedAt\":\"2024-01-01T00:00:00Z\",\"$appVersion\":\"antiga\",\"data\":" + payload + "}";

        storage.Write(SaveManager.ActiveProfileId, "demo", Encoding.UTF8.GetBytes(json));
        return json;
    }

    /// <summary>
    /// Apaga a pasta de saves inteira e recomeça: todos os slots, todos os arquivos, o índice e os
    /// backups. É o botão para quando o estado do teste ficou confuso demais para raciocinar.
    /// </summary>
    private void ResetEverything()
    {
        var storage = new FileSaveStorage(ImplicitSaveSettings.Instance.SaveFolderName);
        var root = storage.RootPath;

        SaveManager.Shutdown();

        if (System.IO.Directory.Exists(root))
        {
            System.IO.Directory.Delete(root, recursive: true);
        }

        _log.Clear();
        Say("Apagado tudo em " + root);
        Say("Slot ativo: " + SaveManager.ActiveProfileId + " · nenhum arquivo, estado de instalação nova.");
        Say("No Save Editor clique em Refresh: todos os saves voltam com ○ de 'sem arquivo'.");
    }

    private static string ReadRaw()
    {
        var storage = new FileSaveStorage(ImplicitSaveSettings.Instance.SaveFolderName);
        var path = storage.GetSavePath(SaveManager.ActiveProfileId, "demo");
        return System.IO.File.Exists(path) ? System.IO.File.ReadAllText(path) : null;
    }

    /// <summary>Conta gravações pela data de modificação — o suficiente para ver o tick pular.</summary>
    private static long CountWrites()
    {
        var storage = new FileSaveStorage(ImplicitSaveSettings.Instance.SaveFolderName);
        var path = storage.GetSavePath(SaveManager.ActiveProfileId, "demo");
        return System.IO.File.Exists(path) ? System.IO.File.GetLastWriteTimeUtc(path).Ticks : 0;
    }

    private static GUIStyle EditorLikeHeader()
    {
        return new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold, fontSize = 12 };
    }

    private void Say(string message)
    {
        _log.Add(DateTime.Now.ToString("HH:mm:ss") + "  " + message);
        Debug.Log("[Playground] " + message);
    }
}
