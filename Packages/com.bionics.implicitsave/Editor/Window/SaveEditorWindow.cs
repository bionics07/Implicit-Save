using System;
using System.Collections.Generic;
using ImplicitSave.Serialization;
using UnityEditor;
using UnityEngine;

namespace ImplicitSave.Editor
{
    /// <summary>
    /// Browse and edit the project's save files: pick a profile, pick a save, change values, apply.
    /// </summary>
    /// <remarks>
    /// Drawing goes through Unity's own inspector rather than through the JSON, which is what makes
    /// custom property drawers, undo, arrays and nesting work without any of it being reimplemented
    /// here.
    /// <para>
    /// Changes are explicit. Writing on every keystroke would persist half-typed state, and a save
    /// file is not the place to find out what a partially typed number does.
    /// </para>
    /// </remarks>
    public class SaveEditorWindow : EditorWindow
    {
        private const float ListWidth = 220f;

        private SaveEditorSession _session;
        private SaveProxy _proxy;
        private SerializedObject _serializedProxy;

        private IReadOnlyList<SaveEntry> _entries;
        private Type _selectedType;
        private int _profileId;
        private bool _isLive;

        /// <summary>
        /// The save exactly as it was loaded, so a field can be compared against where it started.
        /// </summary>
        /// <remarks>
        /// Remembering which fields were TOUCHED would have been less code, but it marks a value you
        /// typed over and put back - and a marker that lies is worse than none. Comparing against a
        /// copy of the original means the mark appears when the value differs and goes away when it
        /// stops differing, which is what someone reading it will assume it means.
        /// </remarks>
        private SaveProxy _baseline;

        private SerializedObject _serializedBaseline;
        /// <summary>
        /// Whether the save on screen differs from the file. Recomputed once per repaint from the
        /// baseline, never latched.
        /// </summary>
        /// <remarks>
        /// This started as a flag that was set on the first edit and only cleared on apply, which
        /// meant the header and the Apply button kept insisting there were changes after a value was
        /// typed back to what it had been - while the field's own asterisk, which compares properly,
        /// had already gone. Two answers to one question, disagreeing on screen.
        /// </remarks>
        private bool _hasPendingChanges;

        /// <summary>
        /// Whether anything has been edited at all since the save was loaded or applied.
        /// </summary>
        /// <remarks>
        /// Only a gate for the comparison, not an answer in itself. Until something is touched there
        /// is nothing to compare, and skipping it keeps a deep compare of the whole save off the
        /// repaint path - which matters in play mode, where the window repaints ten times a second.
        /// </remarks>
        private bool _touched;
        private string _filter = string.Empty;
        private bool _showEditorOnly;
        private string _message;
        private MessageType _messageType = MessageType.Info;

        private Vector2 _listScroll;
        private Vector2 _detailScroll;

        [MenuItem("Tools/ImplicitSave/Save Editor")]
        private static void Open()
        {
            var window = GetWindow<SaveEditorWindow>();
            window.titleContent = new GUIContent("Save Editor");
            window.minSize = new Vector2(680, 360);
            window.Show();
        }

        private void OnEnable()
        {
            _session = new SaveEditorSession();
            Refresh();

            // Entering or leaving play mode swaps what the window is even editing - a file on disk
            // versus the running game's live instance. Rebuilding is safer than trying to carry a
            // half-edited state across.
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            Undo.undoRedoPerformed += OnUndoRedo;
        }

        private void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            Undo.undoRedoPerformed -= OnUndoRedo;
            DestroyProxy();
        }

        /// <summary>
        /// Rebinds the window when the game starts or stops.
        /// </summary>
        /// <remarks>
        /// Crossing this line changes WHAT a selected save means: while the game runs the window
        /// edits the live instance the game is holding, and outside play mode it edits the file. The
        /// old proxy points at the wrong one of those, so it is dropped and the selection is loaded
        /// again from scratch.
        /// <para>
        /// Selecting again is the part that has to be explicit. <see cref="Refresh"/> leaves an
        /// existing selection alone - it is for picking up new saves in the list, not for throwing
        /// away what you were editing - so on its own it would leave the window with no proxy at
        /// all, silently editing nothing.
        /// </para>
        /// </remarks>
        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            var previous = _selectedType;

            DestroyProxy();
            _hasPendingChanges = false;
            _touched = false;
            _session = new SaveEditorSession();
            _entries = _session.ListSaves(_profileId, _showEditorOnly);

            if (IsListed(previous))
            {
                Select(previous);
            }
            else
            {
                _selectedType = null;
                Refresh();
            }

            Repaint();
        }

        private void OnInspectorUpdate()
        {
            // While the game runs it can create or switch slots at any moment. Ten repaints a second
            // is enough for the toolbar to keep up without polling the disk every frame.
            if (SaveEditorSession.IsLive)
            {
                Repaint();
            }
        }

        private void OnUndoRedo()
        {
            if (_serializedProxy != null)
            {
                _serializedProxy.Update();
                _touched = true;
            }

            Repaint();
        }

        private void Refresh()
        {
            _entries = _session.ListSaves(_profileId, _showEditorOnly);

            if (_selectedType != null)
            {
                return;
            }

            if (_entries.Count > 0)
            {
                Select(_entries[0].Type);
            }
        }

        private void Select(Type type)
        {
            DestroyProxy();

            _selectedType = type;
            _hasPendingChanges = false;
            _touched = false;

            if (type == null)
            {
                return;
            }

            try
            {
                var data = _session.Load(type, _profileId, out _isLive);
                _proxy = SaveProxy.Create(data);
                _serializedProxy = new SerializedObject(_proxy);
                CaptureBaseline(data);
                SetMessage(null, MessageType.Info);
            }
            catch (SaveException e)
            {
                SetMessage(e.Message, MessageType.Error);
            }
        }

        /// <summary>Takes the copy every field is compared against.</summary>
        private void CaptureBaseline(SaveData data)
        {
            DestroyBaseline();

            var copy = _session.Clone(data);

            if (copy == null)
            {
                // A save the serializer cannot round-trip. The window still works; it just cannot
                // point at which fields differ.
                return;
            }

            _baseline = SaveProxy.Create(copy);
            _serializedBaseline = new SerializedObject(_baseline);
        }

        private void DestroyBaseline()
        {
            if (_serializedBaseline != null)
            {
                _serializedBaseline.Dispose();
                _serializedBaseline = null;
            }

            if (_baseline != null)
            {
                DestroyImmediate(_baseline);
                _baseline = null;
            }
        }

        /// <summary>
        /// Works out, once per repaint, whether the save on screen still differs from the file.
        /// </summary>
        /// <remarks>
        /// Once per repaint rather than per read: the header, the Apply button and the refresh
        /// prompt all ask, and each answer costs a deep comparison of the whole save.
        /// <para>
        /// With no baseline to compare against - a save the serializer could not round-trip - having
        /// been edited has to count as changed. Refusing to apply a change someone made would be a
        /// worse failure than offering to apply one that turns out to be identical.
        /// </para>
        /// </remarks>
        private void RecomputePendingChanges()
        {
            if (!_touched || _serializedProxy == null)
            {
                _hasPendingChanges = false;
                return;
            }

            if (_serializedBaseline == null)
            {
                _hasPendingChanges = true;
                return;
            }

            _serializedProxy.Update();

            var current = _serializedProxy.FindProperty(nameof(SaveProxy.Data));
            var original = _serializedBaseline.FindProperty(nameof(SaveProxy.Data));

            _hasPendingChanges = current == null || original == null
                                 || !SerializedProperty.DataEquals(current, original);
        }

        /// <summary>Whether this field differs from the value it was loaded with.</summary>
        private bool IsFieldChanged(SerializedProperty property)
        {
            // Nothing pending means nothing can differ, and this short-circuit is what keeps a deep
            // comparison off the repaint path for the whole time the window is just being read.
            if (!_touched || _serializedBaseline == null)
            {
                return false;
            }

            var original = _serializedBaseline.FindProperty(property.propertyPath);

            return original != null && !SerializedProperty.DataEquals(property, original);
        }

        private void DestroyProxy()
        {
            DestroyBaseline();

            if (_serializedProxy != null)
            {
                _serializedProxy.Dispose();
                _serializedProxy = null;
            }

            if (_proxy != null)
            {
                DestroyImmediate(_proxy);
                _proxy = null;
            }
        }

        private void OnGUI()
        {
            if (_session == null)
            {
                _session = new SaveEditorSession();
                Refresh();
            }

            SaveEditorStyles.EnsureBuilt();
            RecomputePendingChanges();

            DrawToolbar();

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawList();
                DrawDetail();
            }
        }

        private void DrawToolbar()
        {
            DrawProfileBar();
            DrawToolsBar();
        }

        private void DrawProfileBar()
        {
            var profiles = _session.GetProfiles();
            var activeId = _session.GetActiveProfileId();

            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                EditorGUILayout.LabelField("Slot", EditorStyles.miniLabel, GUILayout.Width(28));

                var labels = new string[profiles.Count];
                var current = 0;

                for (var i = 0; i < profiles.Count; i++)
                {
                    // The dot marks the slot the game itself uses; the dropdown only chooses what
                    // this window is looking at, and confusing the two is easy.
                    labels[i] = (profiles[i].Id == activeId ? "● " : "    ") +
                                profiles[i].DisplayName + "  (id " + profiles[i].Id + ")" +
                                (profiles[i].HasFiles ? string.Empty : "  ○ empty");

                    if (profiles[i].Id == _profileId)
                    {
                        current = i;
                    }
                }

                var picked = EditorGUILayout.Popup(current, labels, EditorStyles.toolbarPopup, GUILayout.Width(210));

                if (picked != current)
                {
                    SwitchProfile(profiles[picked].Id);
                }

                DrawRenameField(profiles, current);

                if (GUILayout.Button(
                        new GUIContent("+ New", "Create an empty slot. Nothing is written into it until a save is."),
                        EditorStyles.toolbarButton, GUILayout.Width(50)))
                {
                    CreateProfile();
                }

                using (new EditorGUI.DisabledScope(profiles.Count == 0))
                {
                    if (GUILayout.Button(
                            new GUIContent("Duplicate",
                                $"Copy every save of slot {_profileId} into a new slot. The originals are untouched."),
                            EditorStyles.toolbarButton, GUILayout.Width(70)))
                    {
                        DuplicateProfile();
                    }
                }

                using (new EditorGUI.DisabledScope(_profileId == activeId))
                {
                    if (GUILayout.Button(
                            new GUIContent("Set active",
                                "Make this the slot the game reads from. Stored in profiles.json, so the game " +
                                "and this window agree."),
                            EditorStyles.toolbarButton, GUILayout.Width(72)))
                    {
                        SetActiveProfile();
                    }
                }

                using (new EditorGUI.DisabledScope(profiles.Count <= 1))
                {
                    // Tinted rather than filled: a solid red button in a Unity toolbar reads as an
                    // error state, not as an action.
                    if (SaveEditorStyles.DangerButtonField(
                            new GUIContent("Delete slot",
                                $"Delete slot {_profileId} and every save file in it. Cannot be undone."),
                            EditorStyles.toolbarButton, GUILayout.Width(76)))
                    {
                        DeleteProfile();
                    }
                }

                GUILayout.FlexibleSpace();

                if (_isLive)
                {
                    SaveEditorStyles.WithContentColor(SaveEditorStyles.ActiveText, () =>
                        GUILayout.Label("● live — editing the running game", EditorStyles.miniLabel));
                }
            }
        }

        private void DrawRenameField(IReadOnlyList<EditorProfile> profiles, int current)
        {
            if (profiles.Count == 0)
            {
                return;
            }

            var name = profiles[current].DisplayName;
            var edited = EditorGUILayout.DelayedTextField(name, EditorStyles.toolbarTextField, GUILayout.Width(120));

            if (edited != name && !string.IsNullOrWhiteSpace(edited))
            {
                _session.RenameProfile(profiles[current].Id, edited);
                Refresh();
            }
        }

        private void DrawToolsBar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button(
                        new GUIContent("Refresh", "Re-read the save files from disk."),
                        EditorStyles.toolbarButton, GUILayout.Width(64)))
                {
                    if (ConfirmDiscardingEdits())
                    {
                        Select(_selectedType);
                        Refresh();
                    }
                }

                if (GUILayout.Button(
                        new GUIContent("Open folder", "Show this slot's folder in the file browser."),
                        EditorStyles.toolbarButton, GUILayout.Width(84)))
                {
                    _session.RevealSaveFolder(_profileId);
                }

                var showEditorOnly = GUILayout.Toggle(
                    _showEditorOnly,
                    new GUIContent("Editor-only types",
                        "Also list save types from editor and test assemblies. They are hidden by default " +
                        "because they do not exist in a build, so a shipped game never has files for them."),
                    EditorStyles.toolbarButton,
                    GUILayout.Width(120));

                if (showEditorOnly != _showEditorOnly)
                {
                    _showEditorOnly = showEditorOnly;
                    Refresh();
                }

                GUILayout.FlexibleSpace();
                GUILayout.Label(_session.Storage.GetProfilePath(_profileId), EditorStyles.miniLabel);
            }
        }

        private void CreateProfile()
        {
            var id = _session.CreateProfile(null);
            _profileId = id;
            Refresh();
            Select(_selectedType);
            SetMessage($"Created slot {id}. It has no files until something is saved into it.", MessageType.Info);
        }

        private void DuplicateProfile()
        {
            var source = _profileId;
            var destination = _session.CreateProfile($"Copy of slot {source}");

            _session.DuplicateProfile(source, destination);
            _profileId = destination;
            Refresh();
            Select(_selectedType);
            SetMessage($"Copied slot {source} into slot {destination}.", MessageType.Info);
        }

        private void SetActiveProfile()
        {
            _session.SetActiveProfile(_profileId);
            Refresh();
            SetMessage($"Slot {_profileId} is now the one the game reads from.", MessageType.Info);
        }

        private void DeleteProfile()
        {
            var saveCount = _session.Storage.ListSaveIds(_profileId).Count;

            if (!EditorUtility.DisplayDialog(
                    "Delete slot",
                    $"Delete slot {_profileId} and its {saveCount} save file(s)?\n\n" +
                    $"{_session.Storage.GetProfilePath(_profileId)}\n\nThis cannot be undone.",
                    "Delete slot", "Cancel"))
            {
                return;
            }

            _session.DeleteProfile(_profileId);
            _profileId = _session.GetActiveProfileId();
            Refresh();
            Select(_selectedType);
            SetMessage("Slot deleted.", MessageType.Info);
        }

        private void SwitchProfile(int profileId)
        {
            if (_hasPendingChanges && !EditorUtility.DisplayDialog(
                    "Unapplied changes",
                    "This save has changes that were never applied. Switching profile discards them.",
                    "Discard and switch", "Stay here"))
            {
                return;
            }

            _profileId = profileId;
            _hasPendingChanges = false;
            _touched = false;
            Refresh();
            Select(_selectedType);
        }

        private void DrawList()
        {
            using (var scope = new EditorGUILayout.VerticalScope(GUILayout.Width(ListWidth)))
            {
                // The list sits a shade below the window, the way Unity separates the Hierarchy
                // from the Inspector. Flat grey everywhere gives the eye nothing to hold on to.
                if (Event.current.type == EventType.Repaint)
                {
                    SaveEditorStyles.Fill(scope.rect, SaveEditorStyles.Panel);
                    SaveEditorStyles.Fill(
                        new Rect(scope.rect.xMax - 1f, scope.rect.y, 1f, scope.rect.height),
                        SaveEditorStyles.Border);
                }

                _filter = EditorGUILayout.TextField(_filter, EditorStyles.toolbarSearchField);

                _listScroll = EditorGUILayout.BeginScrollView(_listScroll);

                if (_entries.Count == 0)
                {
                    EditorGUILayout.HelpBox(
                        _showEditorOnly
                            ? "No save types in this project. Declare a class deriving from SaveData and it " +
                              "shows up here on its own."
                            : "No save types that would exist in a build. Declare a class deriving from " +
                              "SaveData, or turn on 'Editor-only types' to include the ones in editor and " +
                              "test assemblies.",
                        MessageType.Info);
                }

                var index = 0;

                foreach (var entry in _entries)
                {
                    if (!string.IsNullOrEmpty(_filter) &&
                        entry.DisplayName.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0
                        && entry.SaveId.IndexOf(_filter, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    DrawListRow(entry, index++);
                }

                DrawOrphans();

                EditorGUILayout.EndScrollView();

                GUILayout.FlexibleSpace();
                DrawListFooter();
            }
        }

        /// <summary>
        /// Lists save files no class claims, so they can be looked at and thrown away.
        /// </summary>
        /// <remarks>
        /// These appear when a <c>[SaveId]</c> changes or a save class is deleted: the id is the file
        /// name, so the game starts looking elsewhere and the old file is simply left behind. That is
        /// the safe thing for the package to do - it cannot know whether the data still matters - but
        /// leaving it INVISIBLE was not, because the only way to clean up was to go find the folder
        /// by hand.
        /// </remarks>
        private void DrawOrphans()
        {
            var orphans = _session.ListOrphanSaves(_profileId);

            if (orphans.Count == 0)
            {
                return;
            }

            GUILayout.Space(8f);
            GUILayout.Label(
                new GUIContent("  no class for these",
                    "Files in this slot that no save class answers for - usually a [SaveId] that " +
                    "changed, or a class that was deleted. The package leaves them alone rather than " +
                    "guessing they are junk."),
                SaveEditorStyles.Footnote);

            foreach (var saveId in orphans)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.Space(6f);
                    GUILayout.Label(
                        new GUIContent(saveId, _session.Storage.GetSavePath(_profileId, saveId)),
                        SaveEditorStyles.Footnote, GUILayout.Height(18f));

                    GUILayout.FlexibleSpace();

                    if (SaveEditorStyles.DangerButtonField(
                            new GUIContent("Delete", $"Delete '{saveId}.json' and its backup from slot {_profileId}."),
                            SaveEditorStyles.DangerButton, GUILayout.Width(58f), GUILayout.Height(17f)))
                    {
                        DeleteOrphan(saveId);
                    }

                    GUILayout.Space(6f);
                }
            }
        }

        private void DeleteOrphan(string saveId)
        {
            var path = _session.Storage.GetSavePath(_profileId, saveId);

            if (!EditorUtility.DisplayDialog(
                    "Delete orphaned save?",
                    $"No class in this project answers to '{saveId}'.\n\n{path}\n\n" +
                    "This is what a renamed [SaveId] leaves behind. If the data still matters, cancel and " +
                    "point a class back at that id; if it does not, this is safe to remove. Deleting cannot " +
                    "be undone.",
                    "Delete", "Cancel"))
            {
                return;
            }

            _session.DeleteById(_profileId, saveId);
            SetMessage($"Deleted '{saveId}.json'.", MessageType.Info);
            Repaint();
        }

        /// <summary>
        /// Asks before a refresh throws away edits that were never applied.
        /// </summary>
        /// <remarks>
        /// Refreshing re-reads the file, which is exactly what the button says it does - but outside
        /// play mode that means unapplied edits are gone, with nothing to say they existed. While the
        /// game runs there is nothing to ask about: the window edits the live instance directly, so
        /// the change is already in effect and re-reading finds it again.
        /// </remarks>
        private bool ConfirmDiscardingEdits()
        {
            if (!_hasPendingChanges || _isLive)
            {
                return true;
            }

            return EditorUtility.DisplayDialog(
                "Discard unapplied changes?",
                "This save has edits that were never applied, and refreshing re-reads the file from disk." +
                "\n\n" + "Apply first if you want to keep them.",
                "Discard and refresh", "Cancel");
        }

        private void DrawListFooter()
        {
            var needsMigration = 0;

            foreach (var entry in _entries)
            {
                if (entry.State == SaveFileState.NeedsMigration)
                {
                    needsMigration++;
                }
            }

            var summary = _entries.Count + (_entries.Count == 1 ? " save" : " saves");

            if (needsMigration > 0)
            {
                summary += " · " + needsMigration + " needs migrating";
            }

            var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(20f));

            if (Event.current.type == EventType.Repaint)
            {
                SaveEditorStyles.Fill(new Rect(rect.x, rect.y, rect.width, 1f), SaveEditorStyles.Border);
            }

            GUI.Label(new Rect(rect.x + 9f, rect.y + 3f, rect.width - 12f, 16f), summary, SaveEditorStyles.Footnote);
        }

        private void DrawListRow(SaveEntry entry, int index)
        {
            var selected = entry.Type == _selectedType;
            var rect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(22f));

            if (Event.current.type == EventType.Repaint)
            {
                if (selected)
                {
                    // Unity's own selection colour, full width, with a brighter left edge - the
                    // same shape the Hierarchy uses, so it reads as selection and not decoration.
                    SaveEditorStyles.Fill(rect, SaveEditorStyles.Selection);
                    SaveEditorStyles.Fill(new Rect(rect.x, rect.y, 2f, rect.height), SaveEditorStyles.SelectionEdge);
                }
                else if (index % 2 == 1)
                {
                    SaveEditorStyles.Fill(rect, SaveEditorStyles.RowAlternate);
                }
            }

            var kind = SaveEditorStyles.BadgeFor(entry.State);
            var badgeWidth = SaveEditorStyles.BadgeWidth(kind);

            var labelRect = new Rect(rect.x, rect.y, rect.width - badgeWidth - 12f, rect.height);
            GUI.Label(labelRect, new GUIContent(entry.DisplayName,
                    $"{entry.Type.FullName}\n\nStored as '{entry.SaveId}.json'. The file is named by the id, " +
                    "not by the class, so renaming the class leaves existing saves where they are."),
                selected ? SaveEditorStyles.RowLabelSelected : SaveEditorStyles.RowLabel);

            if (kind != BadgeKind.None)
            {
                var badgeRect = new Rect(rect.xMax - badgeWidth - 7f, rect.y + 3f, badgeWidth, 16f);
                SaveEditorStyles.DrawBadge(badgeRect, kind, BadgeTooltip(entry), selected);
            }

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                if (!selected)
                {
                    Select(entry.Type);
                }

                Event.current.Use();
            }
        }

        private static string BadgeTooltip(SaveEntry entry)
        {
            switch (entry.State)
            {
                case SaveFileState.NotSavedYet:
                    return "No file yet. Nothing is written until you apply a change.";
                case SaveFileState.NeedsMigration:
                    return $"File is at schema version {entry.FileVersion}, the class is at " +
                           $"{entry.ClassVersion}. It will be migrated when it loads.";
                case SaveFileState.FromNewerVersion:
                    return $"File is at schema version {entry.FileVersion}, newer than this build's " +
                           $"{entry.ClassVersion}. It is read-only so nothing is destroyed.";
                case SaveFileState.Unreadable:
                    return "The file exists but could not be read.";
                default:
                    return string.Empty;
            }
        }

        private void DrawDetail()
        {
            using (new EditorGUILayout.VerticalScope())
            {
                if (_selectedType == null)
                {
                    EditorGUILayout.HelpBox("Pick a save on the left.", MessageType.Info);
                    return;
                }

                DrawDetailHeader();

                if (!string.IsNullOrEmpty(_message))
                {
                    EditorGUILayout.HelpBox(_message, _messageType);
                }

                if (_serializedProxy == null)
                {
                    return;
                }

                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
                DrawFields();
                EditorGUILayout.EndScrollView();

                DrawActions();
            }
        }

        private void DrawDetailHeader()
        {
            var entry = FindEntry(_selectedType);

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.Space(8f);
                GUILayout.Label(new GUIContent(entry.DisplayName, $"{entry.Type.FullName}\n\nFile: {entry.SaveId}.json"),
                    SaveEditorStyles.SaveTitle, GUILayout.Height(22f));

                GUILayout.Space(2f);
                var chip = GUILayoutUtility.GetRect(
                    new GUIContent($"schema v{entry.ClassVersion}"), SaveEditorStyles.SchemaChip,
                    GUILayout.Height(15f), GUILayout.Width(64f));
                chip.y += 4f;

                if (Event.current.type == EventType.Repaint)
                {
                    SaveEditorStyles.Fill(chip, new Color(0f, 0f, 0f, 0.16f));
                }

                GUI.Label(chip, $"schema v{entry.ClassVersion}", SaveEditorStyles.SchemaChip);

                GUILayout.FlexibleSpace();

                if (_hasPendingChanges)
                {
                    // Amber, the same colour migration uses: both mean "look at this before you
                    // move on", and one meaning per colour is the rule.
                    SaveEditorStyles.WithContentColor(SaveEditorStyles.WarningText, () =>
                        GUILayout.Label("● unapplied changes", EditorStyles.miniLabel));
                }

                GUILayout.Space(8f);
            }

            var line = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Height(1f));

            if (Event.current.type == EventType.Repaint)
            {
                SaveEditorStyles.Fill(line, SaveEditorStyles.Border);
            }

            switch (entry.State)
            {
                case SaveFileState.NotSavedYet:
                    GUILayout.Space(4f);
                    GUILayout.Label(
                        "   No file yet. You are looking at the defaults - the file is created the moment you " +
                        "apply a change.",
                        SaveEditorStyles.Footnote);
                    break;
                case SaveFileState.NeedsMigration:
                    SaveEditorStyles.DrawBanner(
                        $"The file on disk is at schema version {entry.FileVersion} and the class is at " +
                        $"{entry.ClassVersion}. What you see below has already been migrated; applying writes " +
                        "it back at the new version.",
                        SaveEditorStyles.Warning);
                    break;
                case SaveFileState.FromNewerVersion:
                    SaveEditorStyles.DrawBanner(
                        $"This file is at schema version {entry.FileVersion}, newer than this build understands " +
                        $"({entry.ClassVersion}). It is read-only and will not be written, so the newer data " +
                        "survives.",
                        SaveEditorStyles.Danger);
                    break;
                case SaveFileState.Unreadable:
                    SaveEditorStyles.DrawBanner(
                        "The file exists but could not be read. At runtime the package would fall back to the " +
                        "backup and keep this file rather than delete it.",
                        SaveEditorStyles.Danger);
                    break;
            }

            DrawSharedReferenceWarning();
        }

        /// <summary>
        /// Warns when one object is reachable from two places in the save, which a file cannot
        /// represent.
        /// </summary>
        /// <remarks>
        /// [SerializeReference] keeps a single shared instance, so this window shows one value in two
        /// places and editing either changes both. JSON has no way to say "the same object again":
        /// saving writes two copies, and loading gives back two objects that drift apart from then
        /// on. Nothing crashes, which is exactly what makes it worth a banner - the failure surfaces
        /// much later as "editing one stopped changing the other".
        /// </remarks>
        private void DrawSharedReferenceWarning()
        {
            if (_proxy == null || _proxy.Data == null)
            {
                return;
            }

            var shared = PolymorphicConverter.FindSharedReferences(_proxy.Data);

            if (shared.Count == 0)
            {
                return;
            }

            var names = new List<string>();

            foreach (var value in shared)
            {
                names.Add(value.GetType().Name);
            }

            SaveEditorStyles.DrawBanner(
                $"The same object is stored in more than one place here ({string.Join(", ", names)}). " +
                "Saving writes a separate copy for each, so after the next load they are no longer the same " +
                "object and editing one will not change the other.",
                SaveEditorStyles.Warning);
        }

        private void DrawFields()
        {
            // The proxy can legitimately be gone - a load that failed, or a transition that has not
            // rebound yet - and drawing is not the place to find that out by throwing.
            if (_serializedProxy == null || _proxy == null)
            {
                EditorGUILayout.HelpBox("Nothing to show.", MessageType.None);
                return;
            }

            _serializedProxy.Update();

            var data = _serializedProxy.FindProperty(nameof(SaveProxy.Data));

            if (data == null || _proxy.Data == null)
            {
                EditorGUILayout.HelpBox("Nothing to show.", MessageType.None);
                return;
            }

            using (new EditorGUI.DisabledScope(_proxy.Data.IsReadOnly))
            {
                EditorGUI.BeginChangeCheck();

                var iterator = data.Copy();
                var end = data.GetEndProperty();

                // Descend into the save's own fields once, then walk siblings. PropertyField draws
                // each field's children itself, so entering them again here would draw them twice.
                var enterChildren = true;

                while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
                {
                    enterChildren = false;

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        // A gutter down the left, the way an editor marks an unsaved file. Reserved
                        // whether or not there is a mark, so nothing shifts sideways as you type.
                        GUILayout.Label(
                            IsFieldChanged(iterator)
                                ? new GUIContent("*", "Edited, and not written to the file yet. Apply writes it.")
                                : GUIContent.none,
                            SaveEditorStyles.ChangeMark, GUILayout.Width(10f));

                        using (new EditorGUILayout.VerticalScope())
                        {
                            // Draws whatever drawer the user declared for the type, which is the
                            // entire reason this window goes through SerializedObject, and adds a
                            // type picker to [SerializeReference] fields on the way past.
                            SubtypePicker.DrawProperty(iterator);
                        }
                    }
                }

                if (EditorGUI.EndChangeCheck())
                {
                    _serializedProxy.ApplyModifiedProperties();
                    _touched = true;
                }
            }
        }

        private void DrawActions()
        {
            var bar = EditorGUILayout.BeginHorizontal();

            if (Event.current.type == EventType.Repaint)
            {
                var band = new Rect(bar.x - 4f, bar.y - 4f, bar.width + 8f, bar.height + 10f);
                SaveEditorStyles.Fill(band, SaveEditorStyles.Panel);
                SaveEditorStyles.Fill(new Rect(band.x, band.y, band.width, 1f), SaveEditorStyles.Border);
            }

            {
                using (new EditorGUI.DisabledScope(!_hasPendingChanges || _proxy.Data.IsReadOnly))
                {
                    // The only filled button in the window. Apply is the one action that changes a
                    // file, so it is the one that gets colour.
                    SaveEditorStyles.WithBackground(SaveEditorStyles.PrimaryTint, () =>
                    {
                        if (GUILayout.Button("Apply", SaveEditorStyles.PrimaryButton, GUILayout.Height(20f)))
                        {
                            ApplyChanges();
                        }
                    });

                    if (GUILayout.Button("Revert", GUILayout.Height(20f)))
                    {
                        Select(_selectedType);
                    }
                }

                GUILayout.FlexibleSpace();

                var path = _session.Storage.GetSavePath(_profileId, FindEntry(_selectedType).SaveId);

                using (new EditorGUI.DisabledScope(_proxy.Data.IsReadOnly))
                {
                    if (GUILayout.Button(new GUIContent("Reset to defaults",
                            "Put every field back to what a new game starts with. Nothing is written until " +
                            "you press Apply."), GUILayout.Height(20f)))
                    {
                        ResetToDefaults();
                    }

                    if (GUILayout.Button(new GUIContent("Export", "Write a copy of this save's file somewhere else."), GUILayout.Height(20f)))
                    {
                        Export();
                    }

                    if (GUILayout.Button(new GUIContent("Import",
                            "Replace this save's file with one from disk. The file is checked first, so a bad " +
                            "one cannot overwrite a good save."), GUILayout.Height(20f)))
                    {
                        Import();
                    }

                    GUILayout.Space(6f);

                    // The scariest button in the window, so it says exactly which file it removes -
                    // and is the only other one carrying colour.
                    if (SaveEditorStyles.DangerButtonField(new GUIContent("Delete file",
                            "Delete this one save file and its .bak backup:\n\n" + path +
                            "\n\nOnly this save, and only in this slot. Other saves and other slots are " +
                            "untouched. Cannot be undone."), SaveEditorStyles.DangerButton,
                            GUILayout.Height(20f)))
                    {
                        DeleteFile();
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void ApplyChanges()
        {
            try
            {
                _serializedProxy.ApplyModifiedProperties();
                _session.Apply(_proxy.Data, _profileId);
                _hasPendingChanges = false;
                _touched = false;

                // What is on screen is now what is in the file, so that becomes the new starting
                // point and every mark clears.
                CaptureBaseline(_proxy.Data);
                Refresh();

                SetMessage(
                    _isLive
                        ? "Applied to the running game."
                        : "Written to " + _session.Storage.GetSavePath(_profileId, FindEntry(_selectedType).SaveId),
                    MessageType.Info);
            }
            catch (SaveException e)
            {
                SetMessage(e.Message, MessageType.Error);
            }
        }

        private void ResetToDefaults()
        {
            Undo.RecordObject(_proxy, "Reset save to defaults");
            _proxy.Data.ResetToDefaults();
            _serializedProxy.Update();
            _touched = true;
            SetMessage("Reset to defaults. Nothing is written until you apply.", MessageType.Info);
        }

        private void Export()
        {
            var entry = FindEntry(_selectedType);
            var json = _session.ReadRawJson(_selectedType, _profileId);

            if (json == null)
            {
                SetMessage("There is no file to export yet.", MessageType.Warning);
                return;
            }

            var path = EditorUtility.SaveFilePanel("Export save", string.Empty, entry.SaveId + ".json", "json");

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            System.IO.File.WriteAllText(path, json);
            SetMessage("Exported to " + path, MessageType.Info);
        }

        private void Import()
        {
            var path = EditorUtility.OpenFilePanel("Import save", string.Empty, "json");

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            try
            {
                _session.ImportRawJson(_selectedType, _profileId, System.IO.File.ReadAllText(path));
                Select(_selectedType);
                Refresh();
                SetMessage("Imported from " + path, MessageType.Info);
            }
            catch (SaveException e)
            {
                SetMessage(e.Message, MessageType.Error);
            }
        }

        private void DeleteFile()
        {
            var entry = FindEntry(_selectedType);

            if (!EditorUtility.DisplayDialog(
                    "Delete save file",
                    $"Delete the save '{entry.SaveId}' of slot {_profileId}?\n\n" +
                    $"{_session.Storage.GetSavePath(_profileId, entry.SaveId)}\n\n" +
                    "Only this file and its backup. The slot and its other saves stay. Cannot be undone.",
                    "Delete file", "Cancel"))
            {
                return;
            }

            _session.Delete(_selectedType, _profileId);
            Select(_selectedType);
            Refresh();
            SetMessage("File deleted.", MessageType.Info);
        }

        /// <summary>
        /// Whether a type is actually in the current list. <see cref="FindEntry"/> cannot answer
        /// this - it invents an entry for anything it does not find, so the detail pane can show a
        /// save that has no file yet.
        /// </summary>
        private bool IsListed(Type type)
        {
            if (type == null)
            {
                return false;
            }

            foreach (var entry in _entries)
            {
                if (entry.Type == type)
                {
                    return true;
                }
            }

            return false;
        }

        private SaveEntry FindEntry(Type type)
        {
            foreach (var entry in _entries)
            {
                if (entry.Type == type)
                {
                    return entry;
                }
            }

            return new SaveEntry(type, SaveTypeDiscovery.ResolveSaveId(type), SaveFileState.NotSavedYet, 0, 1);
        }

        private void SetMessage(string message, MessageType type)
        {
            _message = message;
            _messageType = type;
        }
    }
}
