using System.Collections.Generic;
using System.IO;
using ImplicitSave.Storage;
using UnityEngine;

namespace ImplicitSave.Samples.BasicUsage
{
    /// <summary>
    /// Press Play and use the buttons. Every change goes straight into the live save object, and the
    /// package writes it to disk on its own - on the autosave tick, when the app is paused or loses
    /// focus, and on quit. Stop Play Mode, press Play again, and the values are still there.
    /// </summary>
    public class BasicUsageDemo : MonoBehaviour
    {
        private static readonly string[] Areas = { "Forest", "Caves", "Harbour", "Tower" };

        private readonly List<string> _events = new List<string>();
        private System.Action _pending;
        private Vector2 _scroll;
        private GUIStyle _title;
        private GUIStyle _text;

        private void OnEnable()
        {
            SaveManager.Saved += OnSaved;
            SaveManager.Loaded += OnLoaded;
            SaveManager.Failed += OnFailed;
        }

        private void OnDisable()
        {
            SaveManager.Saved -= OnSaved;
            SaveManager.Loaded -= OnLoaded;
            SaveManager.Failed -= OnFailed;
        }

        private void Update()
        {
            // Buttons queue their work for here. Changing data in the middle of an OnGUI pass can
            // change how many controls the next pass draws, which IMGUI reports as an error.
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
            // Get is cheap: there is one live instance per profile, loaded the first time it is asked for.
            var progress = SaveManager.Get<PlayerProgressSaveData>();

            GUILayout.Label("ImplicitSave - Basic Usage", _title);
            GUILayout.Label(
                "Change something, stop Play Mode, press Play again: it is still there. Nothing on this " +
                "screen calls a save method for that to happen.", _text);

            GUILayout.Space(10);
            GUILayout.Label("<b>PlayerProgressSaveData</b> - profile " + progress.ProfileId, _text);
            GUILayout.Label("Coins: " + progress.Coins + "      Level: " + progress.Level, _text);
            GUILayout.Label("Last checkpoint: " + progress.LastCheckpoint, _text);
            GUILayout.Label("Unlocked areas: " + DescribeAreas(progress.UnlockedAreas), _text);
            GUILayout.Label("Inventory: " + DescribeInventory(progress.Inventory), _text);
            GUILayout.Label("Read from disk " + progress.GetTimesLoaded() + " time(s)", _text);

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+10 coins"))
                {
                    _pending = () => progress.Coins += 10;
                }

                if (GUILayout.Button("Level up"))
                {
                    _pending = () => progress.Level++;
                }

                if (GUILayout.Button("Pick up a potion"))
                {
                    _pending = () =>
                    {
                        progress.Inventory.TryGetValue("potion", out var potions);
                        progress.Inventory["potion"] = potions + 1;
                    };
                }
            }

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Unlock next area"))
                {
                    _pending = () => UnlockNextArea(progress);
                }

                if (GUILayout.Button("Move checkpoint"))
                {
                    _pending = () => progress.LastCheckpoint =
                        new Vector3(UnityEngine.Random.Range(-50, 50), 0f, UnityEngine.Random.Range(-50, 50));
                }
            }

            GUILayout.Space(10);
            GUILayout.Label("<b>By hand, when you want to</b>", _text);

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Save now"))
                {
                    _pending = () => SaveManager.Save<PlayerProgressSaveData>();
                }

                if (GUILayout.Button("Reload from disk"))
                {
                    _pending = () => SaveManager.Reload(SaveManager.ActiveProfileId);
                }

                if (GUILayout.Button("New game"))
                {
                    _pending = () => progress.ResetToDefaults();
                }
            }

            GUILayout.Label(
                "Save now writes immediately - after a checkpoint, say. Reload from disk throws away what " +
                "was not written yet and reads the file again. New game resets the object, and the next " +
                "write replaces the file.", _text);

            GUILayout.Space(10);
            var path = new FileSaveStorage(ImplicitSaveSettings.Instance.SaveFolderName)
                .GetSavePath(SaveManager.ActiveProfileId, PlayerProgressSaveData.Id);

            GUILayout.Label("<b>The file</b>", _text);
            GUILayout.Label(path, _text);
            GUILayout.Label(
                "Plain JSON: open it in any text editor, or in Tools > ImplicitSave > Save Editor, which " +
                "can also edit it while the game runs.", _text);

#if UNITY_EDITOR
            GUI.enabled = File.Exists(path);
            if (GUILayout.Button("Show in file browser"))
            {
                UnityEditor.EditorUtility.RevealInFinder(path);
            }

            GUI.enabled = true;
#endif

            GUILayout.Space(10);
            GUILayout.Label("<b>Events</b>", _text);

            lock (_events)
            {
                GUILayout.Label(_events.Count == 0 ? "Nothing yet." : string.Join("\n", _events), _text);
            }
        }

        private static void UnlockNextArea(PlayerProgressSaveData progress)
        {
            foreach (var area in Areas)
            {
                if (!progress.UnlockedAreas.Contains(area))
                {
                    progress.UnlockedAreas.Add(area);
                    return;
                }
            }
        }

        private static string DescribeAreas(List<string> areas)
        {
            return areas.Count == 0 ? "none" : string.Join(", ", areas);
        }

        private static string DescribeInventory(SerializableDictionary<string, int> inventory)
        {
            if (inventory.Count == 0)
            {
                return "empty";
            }

            var parts = new List<string>();
            foreach (var entry in inventory)
            {
                parts.Add(entry.Key + " x" + entry.Value);
            }

            return string.Join(", ", parts);
        }

        private void OnSaved(System.Type type, int profileId)
        {
            AddEvent("Wrote " + type.Name + " for profile " + profileId);
        }

        private void OnLoaded(System.Type type, int profileId)
        {
            AddEvent("Read " + type.Name + " for profile " + profileId);
        }

        private void OnFailed(SaveException exception)
        {
            AddEvent("Failed: " + exception.Message);
        }

        private void AddEvent(string message)
        {
            // Autosave writes on a background thread, so its event can arrive off the main thread.
            lock (_events)
            {
                _events.Insert(0, System.DateTime.Now.ToString("HH:mm:ss") + "  " + message);
                if (_events.Count > 8)
                {
                    _events.RemoveAt(_events.Count - 1);
                }
            }
        }

        private void EnsureStyles()
        {
            if (_title != null)
            {
                return;
            }

            _title = new GUIStyle(GUI.skin.label) { fontSize = 20, fontStyle = FontStyle.Bold };
            _text = new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true };
        }
    }
}
