using System.Collections.Generic;
using UnityEngine;

namespace ImplicitSave.Samples.MultiProfile
{
    /// <summary>
    /// Save slots. <c>SaveManager.Get&lt;T&gt;()</c> reads the active profile and
    /// <c>SaveManager.Get&lt;T&gt;(id)</c> reads any of them, so a "load game" screen needs no second
    /// save class. On disk each profile is a folder, and <c>profiles.json</c> lists them.
    /// </summary>
    public class MultiProfileDemo : MonoBehaviour
    {
        private System.Action _pending;
        private Vector2 _scroll;
        private int _confirmDeleteId = -1;
        private int _renamingId = -1;
        private string _renameText = "";
        private GUIStyle _title;
        private GUIStyle _text;

        private void Update()
        {
            // Buttons queue their work for here: adding or removing a slot in the middle of an OnGUI
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
            var activeId = SaveManager.ActiveProfileId;
            var active = SaveManager.Get<SlotSaveData>();

            GUILayout.Label("ImplicitSave - Multi Profile", _title);
            GUILayout.Label(
                "Every slot has its own copy of SlotSaveData. Edit the hero, switch slots, and each one " +
                "keeps its own values - across Play sessions too.", _text);

            GUILayout.Space(10);
            GUILayout.Label("<b>Playing on profile " + activeId + "</b>", _text);

            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Label("Hero name", GUILayout.Width(90));
                active.HeroName = GUILayout.TextField(active.HeroName, 24);
            }

            using (new GUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Level up"))
                {
                    _pending = () => active.Level++;
                }

                if (GUILayout.Button("+25 gold"))
                {
                    _pending = () => active.Gold += 25;
                }
            }

            GUILayout.Space(10);
            GUILayout.Label("<b>Slots</b>", _text);

            // A copy: creating or deleting a slot changes the list this would otherwise be walking.
            var profiles = new List<ProfileInfo>(SaveManager.GetProfiles());
            foreach (var profile in profiles)
            {
                DrawSlot(profile, profile.Id == activeId);
            }

            if (GUILayout.Button("New empty slot"))
            {
                _pending = () => SaveManager.CreateProfile();
            }

            GUILayout.Label(
                "Switching writes the slot you are leaving first, so nothing is lost. Deleting removes the " +
                "slot's folder and cannot be undone - a real game should ask the player, as this does.", _text);
        }

        private void DrawSlot(ProfileInfo profile, bool isActive)
        {
            // Reading another profile's save does not switch to it.
            var slot = SaveManager.Get<SlotSaveData>(profile.Id);
            var hero = string.IsNullOrEmpty(slot.HeroName) ? "no hero yet" : slot.HeroName;

            using (new GUILayout.VerticalScope(GUI.skin.box))
            {
                GUILayout.Label(
                    (isActive ? "<b>[playing]</b> " : "") + profile.DisplayName + " (id " + profile.Id + ") - " +
                    hero + ", level " + slot.Level + ", " + slot.Gold + " gold", _text);

                using (new GUILayout.HorizontalScope())
                {
                    GUI.enabled = !isActive;
                    if (GUILayout.Button("Play this slot"))
                    {
                        _pending = () => SaveManager.SetActiveProfile(profile.Id);
                    }

                    GUI.enabled = true;
                    if (GUILayout.Button("Copy to new slot"))
                    {
                        _pending = () =>
                        {
                            var copy = SaveManager.CreateProfile(profile.DisplayName + " (copy)");
                            SaveManager.CopyProfile(profile.Id, copy.Id);
                        };
                    }

                    if (GUILayout.Button("Rename"))
                    {
                        _pending = () =>
                        {
                            _renamingId = profile.Id;
                            _renameText = profile.DisplayName;
                        };
                    }

                    // The slot being played cannot be deleted from under the game. Switch first.
                    GUI.enabled = !isActive;
                    var confirming = _confirmDeleteId == profile.Id;
                    if (GUILayout.Button(confirming ? "Really delete?" : "Delete"))
                    {
                        _pending = () =>
                        {
                            if (confirming)
                            {
                                SaveManager.DeleteProfile(profile.Id);
                                _confirmDeleteId = -1;
                            }
                            else
                            {
                                _confirmDeleteId = profile.Id;
                            }
                        };
                    }

                    GUI.enabled = true;
                }

                if (_renamingId != profile.Id)
                {
                    return;
                }

                using (new GUILayout.HorizontalScope())
                {
                    _renameText = GUILayout.TextField(_renameText, 32);

                    if (GUILayout.Button("OK", GUILayout.Width(60)))
                    {
                        var name = _renameText;
                        _pending = () =>
                        {
                            SaveManager.RenameProfile(profile.Id, name);
                            _renamingId = -1;
                        };
                    }

                    if (GUILayout.Button("Cancel", GUILayout.Width(60)))
                    {
                        _pending = () => _renamingId = -1;
                    }
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
