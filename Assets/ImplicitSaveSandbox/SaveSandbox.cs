using ImplicitSave;
using ImplicitSave.Serialization;
using ImplicitSave.Storage;
using UnityEngine;

/// <summary>
/// Rascunho para experimentar o núcleo da fase 1 na mão. Não faz parte do package - a pasta
/// Assets/ImplicitSaveSandbox existe só para testar e pode ser apagada a qualquer momento.
/// </summary>
/// <remarks>
/// Como usar: crie um GameObject vazio na cena, adicione este componente, e use o menu de contexto
/// do componente (os três pontinhos no Inspector) para chamar cada ação. Funciona sem dar Play.
/// </remarks>
public class SaveSandbox : MonoBehaviour
{
    [Header("Mexa nestes valores e use 'Salvar' no menu de contexto")]
    public int Coins = 10;
    public string LastCheckpoint = "floresta_02";

    [Header("Só leitura - preenchido ao carregar")]
    [SerializeField] private string _filePath;

    private SaveRepository _repository;

    private SaveRepository Repository
    {
        get
        {
            if (_repository == null)
            {
                // É isto que a fachada SaveManager vai montar sozinha numa fase seguinte.
                var storage = new FileSaveStorage("saves");
                _repository = new SaveRepository(new NewtonsoftSaveSerializer(), storage);
                _repository.Failed += e => Debug.LogError("Sandbox recebeu falha: " + e.Message);
                _filePath = storage.GetSavePath(0, "sandbox");
            }

            return _repository;
        }
    }

    [ContextMenu("1. Carregar")]
    public void Load()
    {
        var data = Repository.Get<SandboxSaveData>(0);
        Coins = data.Coins;
        LastCheckpoint = data.LastCheckpoint;
        Debug.Log($"Carregado: Coins={Coins}, LastCheckpoint={LastCheckpoint}");
    }

    [ContextMenu("2. Salvar")]
    public void Save()
    {
        var data = Repository.Get<SandboxSaveData>(0);
        data.Coins = Coins;
        data.LastCheckpoint = LastCheckpoint;
        Repository.Save<SandboxSaveData>(0);
        Debug.Log($"Salvo em {_filePath}");
    }

    [ContextMenu("3. Descartar memoria e recarregar do disco")]
    public void ReloadFromDisk()
    {
        Repository.Reload(0);
        Load();
    }

    [ContextMenu("4. Apagar o save")]
    public void DeleteSave()
    {
        Repository.Storage.Delete(0, "sandbox");
        Repository.Reload(0);
        Debug.Log("Save apagado.");
    }

    [ContextMenu("Mostrar o caminho do arquivo")]
    public void ShowPath()
    {
        Debug.Log(Repository.Storage is FileSaveStorage file
            ? file.GetSavePath(0, "sandbox")
            : "storage nao e de arquivo");
    }
}

/// <summary>
/// Declarar a classe é tudo que o usuário do package precisa escrever - sem registrar em lugar
/// nenhum, sem arrastar referência.
/// </summary>
[SaveId("sandbox")]
public class SandboxSaveData : SaveData
{
    public int Coins;
    public string LastCheckpoint;

    public override void ResetToDefaults()
    {
        Coins = 0;
        LastCheckpoint = "inicio";
    }
}
