using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ImplicitSave.Editor
{
    /// <summary>
    /// The ImplicitSave page under Edit &gt; Project Settings. It edits the settings asset, and creates
    /// it when asked - never on its own: the package runs on defaults without one, and an asset nobody
    /// asked for is clutter in someone else's project.
    /// </summary>
    internal class ImplicitSaveSettingsProvider : SettingsProvider
    {
        internal const string SettingsPath = "Project/ImplicitSave";

        internal const string DefaultAssetPath = "Assets/Resources/" + ImplicitSaveSettings.AssetName + ".asset";

        private SerializedObject _serialized;

        private ImplicitSaveSettingsProvider()
            : base(SettingsPath, SettingsScope.Project, BuildKeywords())
        {
        }

        [SettingsProvider]
        internal static SettingsProvider Create()
        {
            return new ImplicitSaveSettingsProvider();
        }

        [MenuItem("Tools/ImplicitSave/Settings")]
        private static void OpenFromMenu()
        {
            SettingsService.OpenProjectSettings(SettingsPath);
        }

        /// <summary>
        /// The asset the runtime reads. Uses the same lookup as <see cref="ImplicitSaveSettings.Instance"/>,
        /// so the page edits exactly what a build will load.
        /// </summary>
        internal static ImplicitSaveSettings FindAssetInUse()
        {
            return Resources.Load<ImplicitSaveSettings>(ImplicitSaveSettings.AssetName);
        }

        /// <summary>
        /// Settings assets that exist but are not read - misnamed, outside a Resources folder, or a
        /// second copy. Without this list someone edits one of them and wonders why nothing changes.
        /// </summary>
        internal static List<string> FindIgnoredAssetPaths(ImplicitSaveSettings inUse)
        {
            var inUsePath = inUse != null ? AssetDatabase.GetAssetPath(inUse) : null;
            var ignored = new List<string>();

            foreach (var guid in AssetDatabase.FindAssets("t:" + nameof(ImplicitSaveSettings)))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path != inUsePath)
                {
                    ignored.Add(path);
                }
            }

            return ignored;
        }

        internal static ImplicitSaveSettings CreateAsset(string path)
        {
            EnsureFolder(Path.GetDirectoryName(path)?.Replace('\\', '/'));

            var asset = ScriptableObject.CreateInstance<ImplicitSaveSettings>();
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();

            ImplicitSaveSettings.ForgetInstance();
            return asset;
        }

        /// <summary>Settings that are legal to type but do something other than what they suggest.</summary>
        internal static List<string> FindProblems(ImplicitSaveSettings settings)
        {
            var problems = new List<string>();

            if (settings.AutoSaveEnabled && settings.AutoSaveIntervalSeconds <= 0f)
            {
                problems.Add(
                    "Auto Save Interval Seconds is zero or less, which switches the periodic tick off just like " +
                    "unticking Auto Save Enabled. The application hooks still run.");
            }

            var folder = settings.SaveFolderName;
            if (string.IsNullOrWhiteSpace(folder))
            {
                problems.Add(
                    "Save Folder Name is empty, so save files land directly in the persistent data path, next to " +
                    "whatever else the game and its plugins keep there.");
            }
            else if (!IsPlainSubfolder(folder))
            {
                problems.Add(
                    "Save Folder Name has to be a folder inside the persistent data path. '" + folder +
                    "' points outside it or is not a valid folder name.");
            }

            return problems;
        }

        public override void OnDeactivate()
        {
            _serialized = null;
        }

        public override void OnGUI(string searchContext)
        {
            var previousLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 220;

            using (new GUILayout.HorizontalScope())
            {
                GUILayout.Space(10);

                using (new GUILayout.VerticalScope())
                {
                    GUILayout.Space(6);

                    var asset = FindAssetInUse();
                    if (asset == null)
                    {
                        DrawMissingAsset();
                    }
                    else
                    {
                        DrawAsset(asset);
                    }

                    DrawIgnoredAssets(asset);
                }

                GUILayout.Space(10);
            }

            EditorGUIUtility.labelWidth = previousLabelWidth;
        }

        private static void DrawMissingAsset()
        {
            EditorGUILayout.HelpBox(
                "There is no settings asset, so the package is running on its defaults. That is a fine place " +
                "to stay - create the asset only when you want to change one of them.",
                MessageType.Info);

            if (GUILayout.Button("Create Settings Asset", GUILayout.Width(180)))
            {
                EditorGUIUtility.PingObject(CreateAsset(DefaultAssetPath));
            }

            EditorGUILayout.LabelField("Created at " + DefaultAssetPath, EditorStyles.miniLabel);
        }

        private void DrawAsset(ImplicitSaveSettings asset)
        {
            if (_serialized == null || _serialized.targetObject != asset)
            {
                _serialized = new SerializedObject(asset);
            }

            _serialized.Update();

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(AssetDatabase.GetAssetPath(asset), EditorStyles.miniLabel);

                if (GUILayout.Button("Select", EditorStyles.miniButton, GUILayout.Width(60)))
                {
                    Selection.activeObject = asset;
                    EditorGUIUtility.PingObject(asset);
                }
            }

            var property = _serialized.GetIterator();
            for (var enterChildren = true; property.NextVisible(enterChildren); enterChildren = false)
            {
                if (property.propertyPath == "m_Script")
                {
                    continue;
                }

                EditorGUILayout.PropertyField(property, true);
            }

            if (_serialized.ApplyModifiedProperties())
            {
                // The log switch is copied once, when settings load. Keep it in step with the checkbox.
                ImplicitSaveLog.VerboseLogging = asset.VerboseLogging;
            }

            EditorGUILayout.Space(6);
            foreach (var problem in FindProblems(asset))
            {
                EditorGUILayout.HelpBox(problem, MessageType.Warning);
            }
        }

        private static void DrawIgnoredAssets(ImplicitSaveSettings inUse)
        {
            var ignored = FindIgnoredAssetPaths(inUse);
            if (ignored.Count == 0)
            {
                return;
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.HelpBox(
                "Only one settings asset is read: the one named " + ImplicitSaveSettings.AssetName +
                " inside a Resources folder. These are not being used:\n" + string.Join("\n", ignored),
                MessageType.Warning);
        }

        private static IEnumerable<string> BuildKeywords()
        {
            // Every setting's display name, so typing any of them in the search box lands here.
            var keywords = new List<string> { "save", "autosave", "json" };

            foreach (var field in typeof(ImplicitSaveSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                keywords.Add(ObjectNames.NicifyVariableName(field.Name));
            }

            return keywords;
        }

        private static bool IsPlainSubfolder(string folder)
        {
            if (Path.IsPathRooted(folder))
            {
                return false;
            }

            foreach (var segment in folder.Split('/', '\\'))
            {
                if (segment.Length == 0 || segment == "." || segment == ".." ||
                    segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                {
                    return false;
                }
            }

            return true;
        }

        private static void EnsureFolder(string folder)
        {
            if (string.IsNullOrEmpty(folder) || AssetDatabase.IsValidFolder(folder))
            {
                return;
            }

            var parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
        }
    }
}
