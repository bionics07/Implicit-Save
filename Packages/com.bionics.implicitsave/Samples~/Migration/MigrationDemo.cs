using System.IO;
using System.Text;
using ImplicitSave.Serialization;
using ImplicitSave.Storage;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace ImplicitSave.Samples.Migration
{
    /// <summary>
    /// Puts a file from an older build on disk, the way a player who skipped updates would have it,
    /// and shows it loading into the current <see cref="CharacterSaveData"/>. A file from a newer
    /// build is there too, to show it is kept rather than overwritten.
    /// </summary>
    public class MigrationDemo : MonoBehaviour
    {
        private System.Action _pending;
        private Vector2 _scroll;
        private string _onDisk;
        private string _lastFailure;
        private GUIStyle _title;
        private GUIStyle _text;
        private GUIStyle _code;

        private void OnEnable()
        {
            SaveManager.Failed += OnFailed;
            _onDisk = ReadFile();
        }

        private void OnDisable()
        {
            SaveManager.Failed -= OnFailed;
        }

        private void Update()
        {
            // Buttons queue their work for here rather than changing data in the middle of an OnGUI pass.
            var action = _pending;
            _pending = null;
            action?.Invoke();
        }

        private void OnGUI()
        {
            EnsureStyles();

            var scale = Mathf.Max(1f, Screen.height / 720f);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));

            var width = Mathf.Min(620f, Screen.width / scale - 32f);
            GUILayout.BeginArea(new Rect(16f, 16f, width, Screen.height / scale - 32f), GUI.skin.box);
            _scroll = GUILayout.BeginScrollView(_scroll);
            Draw();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void Draw()
        {
            GUILayout.Label("ImplicitSave - Migration", _title);
            GUILayout.Label(
                "CharacterSaveData is at version 3. Version 1 called health \"Hp\", and versions 1 and 2 " +
                "kept the whole name in one field. Two migrations carry old files forward.", _text);

            GUILayout.Space(10);
            GUILayout.Label("<b>1. Put a file on disk</b>", _text);

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Version 1 file"))
                {
                    _pending = () => WriteFile(1, new JObject { ["Name"] = "Ada Lovelace", ["Hp"] = 142 });
                }

                if (GUILayout.Button("Version 2 file"))
                {
                    _pending = () => WriteFile(2, new JObject
                    {
                        ["Name"] = "Grace Brewster Hopper", ["Health"] = 80, ["MaxHealth"] = 120
                    });
                }

                if (GUILayout.Button("Version 99 file"))
                {
                    _pending = () => WriteFile(99, new JObject { ["DisplayName"] = "From a future update", ["Mana"] = 7 });
                }
            }

            GUILayout.Label(
                "Each button replaces this profile's file and then drops what was loaded, so the next read " +
                "goes through the file.", _text);

            GUILayout.TextArea(_onDisk ?? "No file yet.", _code, GUILayout.MinHeight(120));

            GUILayout.Space(10);
            GUILayout.Label("<b>2. What the game gets</b>", _text);

            var character = SaveManager.Get<CharacterSaveData>();
            GUILayout.Label("First name: " + character.FirstName + "      Last name: " + character.LastName, _text);
            GUILayout.Label("Health: " + character.Health + " / " + character.MaxHealth, _text);
            GUILayout.Label(
                character.IsReadOnly
                    ? "<b>Read-only.</b> The file came from a newer build. It is never written, not even by a " +
                      "forced save, so the progress in it survives until the player updates."
                    : "Writable.", _text);

            if (!string.IsNullOrEmpty(_lastFailure))
            {
                GUILayout.Label("Last failure: " + _lastFailure, _text);
            }

            GUILayout.Space(10);
            GUILayout.Label("<b>3. Write it back</b>", _text);

            if (GUILayout.Button("Save"))
            {
                _pending = () =>
                {
                    SaveManager.Save<CharacterSaveData>();
                    _onDisk = ReadFile();
                };
            }

            GUILayout.Label(
                "Loading migrates in memory only: the file keeps its old shape until the next write, which " +
                "stores version 3. The file it replaces is kept alongside as .bak.", _text);
        }

        private void WriteFile(int schemaVersion, JObject payload)
        {
            // What an older build would have written: the envelope with its version, and the payload.
            var envelope = new JObject
            {
                [SaveEnvelope.SaveIdKey] = CharacterSaveData.Id,
                [SaveEnvelope.SchemaVersionKey] = schemaVersion,
                [SaveEnvelope.SavedAtKey] = "2024-01-01T00:00:00Z",
                ["$appVersion"] = "an older build",
                [SaveEnvelope.DataKey] = payload
            };

            var profileId = SaveManager.ActiveProfileId;
            OpenStorage().Write(profileId, CharacterSaveData.Id, Encoding.UTF8.GetBytes(envelope.ToString()));

            // Drops every save of this profile from memory - fine for a demo, and the only way to make
            // the next Get read the file that was just put there.
            SaveManager.Reload(profileId);

            _lastFailure = null;
            _onDisk = ReadFile();
        }

        private static string ReadFile()
        {
            var path = OpenStorage().GetSavePath(SaveManager.ActiveProfileId, CharacterSaveData.Id);
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }

        private static FileSaveStorage OpenStorage()
        {
            return new FileSaveStorage(ImplicitSaveSettings.Instance.SaveFolderName);
        }

        private void OnFailed(SaveException exception)
        {
            _lastFailure = exception.Message;
        }

        private void EnsureStyles()
        {
            if (_title != null)
            {
                return;
            }

            _title = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            _text = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true };
            _code = new GUIStyle(GUI.skin.textArea) { wordWrap = true };
        }
    }
}
