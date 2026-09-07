using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ImplicitSave.Editor
{
    /// <summary>
    /// Lists every save type the package found, where each one came from, and what it will be called
    /// on disk.
    /// </summary>
    /// <remarks>
    /// "Implicit" reads as "magic I cannot debug" to a lot of developers, and that is the main
    /// objection to this package's whole idea. Being able to open a window and see the list - with
    /// the generated file one click away - is the answer to it, so this is a product requirement
    /// rather than a convenience.
    /// </remarks>
    public class SaveTypesWindow : EditorWindow
    {
        private Vector2 _scroll;

        [MenuItem("Tools/ImplicitSave/Save Types")]
        private static void Open()
        {
            var window = GetWindow<SaveTypesWindow>();
            window.titleContent = new GUIContent("Save Types");
            window.minSize = new Vector2(460, 240);
            window.Show();
        }

        private void OnGUI()
        {
            var saveTypes = SaveTypeDiscovery.FindBuildableSaveTypes();
            var subtypes = SaveTypeDiscovery.FindBuildableSubtypes();
            var excluded = SaveTypeDiscovery.FindExcludedTypes();

            DrawToolbar();

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.HelpBox(
                "These were found by scanning the project - nothing here was registered by hand. " +
                "A build cannot scan, so the same list is written out to SaveRegistry.g.cs.",
                MessageType.Info);

            DrawSection("Save files", saveTypes, SaveTypeDiscovery.ResolveSaveId, "file name");
            DrawSection("Polymorphic subtypes", subtypes, SaveTypeDiscovery.ResolveSubtypeId, "$t value");

            if (saveTypes.Count == 0 && subtypes.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    "No save types yet. Declare a class deriving from ImplicitSave.SaveData and it will " +
                    "show up here - there is nothing else to do.",
                    MessageType.None);
            }

            DrawExcluded(excluded);
            DrawProblems(saveTypes, subtypes);
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("Regenerate", EditorStyles.toolbarButton, GUILayout.Width(90)))
                {
                    SaveRegistryGenerator.Generate();
                }

                var exists = File.Exists(SaveRegistryGenerator.RegistryPath);
                using (new EditorGUI.DisabledScope(!exists))
                {
                    if (GUILayout.Button("Open generated file", EditorStyles.toolbarButton, GUILayout.Width(140)))
                    {
                        var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(SaveRegistryGenerator.RegistryPath);
                        AssetDatabase.OpenAsset(asset);
                    }
                }

                GUILayout.FlexibleSpace();

                GUILayout.Label(
                    exists ? SaveRegistryGenerator.RegistryPath : "not generated yet",
                    EditorStyles.miniLabel);
            }
        }

        private static void DrawSection(
            string title, System.Collections.Generic.IReadOnlyList<Type> types, Func<Type, string> resolveId, string idLabel)
        {
            if (types.Count == 0)
            {
                return;
            }

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField($"{title} ({types.Count})", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(idLabel, EditorStyles.miniBoldLabel, GUILayout.Width(160));
                EditorGUILayout.LabelField("type", EditorStyles.miniBoldLabel);
                EditorGUILayout.LabelField("assembly", EditorStyles.miniBoldLabel, GUILayout.Width(160));
            }

            foreach (var type in types)
            {
                var declared = Attribute.IsDefined(type, typeof(SaveIdAttribute), false)
                               || Attribute.IsDefined(type, typeof(SaveTypeAttribute), false);

                using (new EditorGUILayout.HorizontalScope())
                {
                    var id = resolveId(type);
                    var label = declared
                        ? new GUIContent(id)
                        : new GUIContent(id + "  (inferred)",
                            "This id was derived from the class name because there is no attribute on it. " +
                            "Renaming the class would orphan every save already written.");

                    EditorGUILayout.LabelField(label, GUILayout.Width(160));
                    EditorGUILayout.LabelField(type.FullName);
                    EditorGUILayout.LabelField(type.Assembly.GetName().Name, GUILayout.Width(160));
                }
            }
        }

        private static void DrawExcluded(
            System.Collections.Generic.IReadOnlyList<System.Collections.Generic.KeyValuePair<Type, string>> excluded)
        {
            if (excluded.Count == 0)
            {
                return;
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField($"Visible here, absent from a build ({excluded.Count})", EditorStyles.boldLabel);

            foreach (var entry in excluded)
            {
                EditorGUILayout.HelpBox(entry.Key.FullName + " - " + entry.Value, MessageType.Warning);
            }
        }

        private static void DrawProblems(
            System.Collections.Generic.IReadOnlyList<Type> saveTypes,
            System.Collections.Generic.IReadOnlyList<Type> subtypes)
        {
            var problems = SaveRegistryGenerator.Validate(saveTypes, subtypes);
            if (problems.Count == 0)
            {
                return;
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Problems that will fail a build", EditorStyles.boldLabel);

            foreach (var problem in problems)
            {
                EditorGUILayout.HelpBox(problem, MessageType.Error);
            }
        }
    }
}
