using System.IO;
using ImplicitSave.Storage;
using UnityEngine;

namespace ImplicitSave.Samples.Polymorphism
{
    /// <summary>
    /// Fill the backpack with a mix of items, equip one, then write the file and look at it: every
    /// value carries a <c>"$t"</c> with its subtype's id. Stop and press Play again, and each item
    /// comes back as the type it was.
    /// </summary>
    public class PolymorphismDemo : MonoBehaviour
    {
        private static readonly string[] SwordNames = { "Iron Sword", "Rusty Blade", "Moonsteel Sabre" };
        private static readonly string[] PotionNames = { "Red Potion", "Herbal Tonic", "Elixir" };
        private static readonly string[] BowNames = { "Short Bow", "Yew Longbow", "Hunter's Bow" };

        private System.Action _pending;
        private Vector2 _scroll;
        private string _fileText;
        private GUIStyle _title;
        private GUIStyle _text;
        private GUIStyle _code;

        private void Update()
        {
            // Buttons queue their work for here: adding or removing an item in the middle of an OnGUI
            // pass changes how many controls the next pass draws, which IMGUI reports as an error.
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
            var loadout = SaveManager.Get<LoadoutSaveData>();

            GUILayout.Label("ImplicitSave - Polymorphism", _title);
            GUILayout.Label(
                "Backpack is a List<Item> and Equipped is an Item, yet swords, potions and bows keep what " +
                "makes them different through a save.", _text);

            GUILayout.Space(10);
            GUILayout.Label("<b>Equipped:</b> " + (loadout.Equipped != null ? loadout.Equipped.Describe() : "nothing"), _text);

            GUI.enabled = loadout.Equipped != null;
            if (GUILayout.Button("Unequip"))
            {
                _pending = () =>
                {
                    loadout.Backpack.Add(loadout.Equipped);
                    loadout.Equipped = null;
                };
            }

            GUI.enabled = true;

            GUILayout.Space(10);
            GUILayout.Label("<b>Backpack</b> (" + loadout.Backpack.Count + ")", _text);

            if (loadout.Backpack.Count == 0)
            {
                GUILayout.Label("Empty. Find something below.", _text);
            }

            for (var i = 0; i < loadout.Backpack.Count; i++)
            {
                var index = i;
                var item = loadout.Backpack[i];

                using (new GUILayout.HorizontalScope())
                {
                    GUILayout.Label(item != null ? item.Describe() : "(empty slot)", _text);

                    if (GUILayout.Button("Equip", GUILayout.Width(70)))
                    {
                        _pending = () => Equip(loadout, index);
                    }

                    if (GUILayout.Button("Drop", GUILayout.Width(60)))
                    {
                        _pending = () => loadout.Backpack.RemoveAt(index);
                    }
                }
            }

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Find a sword"))
                {
                    _pending = () => loadout.Backpack.Add(NewSword());
                }

                if (GUILayout.Button("Find a potion"))
                {
                    _pending = () => loadout.Backpack.Add(NewPotion());
                }

                if (GUILayout.Button("Find a bow"))
                {
                    _pending = () => loadout.Backpack.Add(NewBow());
                }
            }

            GUILayout.Label(
                "Equipping moves the item out of the backpack instead of pointing both at one object. Two " +
                "fields sharing an object would come back from the file as two separate copies - the Save " +
                "Editor warns when that happens.", _text);

            GUILayout.Space(10);
            GUILayout.Label("<b>The file</b>", _text);

            if (GUILayout.Button("Save and show the file"))
            {
                _pending = () =>
                {
                    SaveManager.Save<LoadoutSaveData>();
                    _fileText = ReadFile();
                };
            }

            GUILayout.TextArea(
                _fileText ?? "Press the button to write the file and see it here. Each \"$t\" is the [SaveType] id " +
                "of that value - an id, never a class name, so renaming a class does not break saves.",
                _code, GUILayout.MinHeight(180));
        }

        private static void Equip(LoadoutSaveData loadout, int index)
        {
            var item = loadout.Backpack[index];
            loadout.Backpack.RemoveAt(index);

            if (loadout.Equipped != null)
            {
                loadout.Backpack.Add(loadout.Equipped);
            }

            loadout.Equipped = item;
        }

        private static Sword NewSword()
        {
            return new Sword { Name = Pick(SwordNames), Damage = Random.Range(5, 40) };
        }

        private static Potion NewPotion()
        {
            return new Potion { Name = Pick(PotionNames), Healing = Random.Range(10, 60), Doses = Random.Range(1, 4) };
        }

        private static Bow NewBow()
        {
            // Half the bows carry a potion strapped on - a polymorphic value inside a polymorphic value.
            return new Bow
            {
                Name = Pick(BowNames),
                Range = Random.Range(20, 90),
                Strapped = Random.value < 0.5f ? NewPotion() : null
            };
        }

        private static string Pick(string[] options)
        {
            return options[Random.Range(0, options.Length)];
        }

        private static string ReadFile()
        {
            var path = new FileSaveStorage(ImplicitSaveSettings.Instance.SaveFolderName)
                .GetSavePath(SaveManager.ActiveProfileId, LoadoutSaveData.Id);

            return File.Exists(path) ? File.ReadAllText(path) : "No file yet.";
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
