using System.Linq;
using System.Reflection;
using ImplicitSave.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ImplicitSave.Tests.EditorTests
{
    /// <summary>
    /// The Project Settings page: where it lives, that search reaches it, what creating the asset
    /// does to the settings already in use, and which values it warns about.
    /// </summary>
    public class ImplicitSaveSettingsProviderTests
    {
        private const string TempRoot = "Assets/ImplicitSaveSettingsProviderTests";

        [TearDown]
        public void TearDown()
        {
            AssetDatabase.DeleteAsset(TempRoot);
            ImplicitSaveSettings.ForgetInstance();
        }

        [Test]
        public void ThePageLivesUnderProjectSettings()
        {
            var provider = ImplicitSaveSettingsProvider.Create();

            Assert.That(provider.settingsPath, Is.EqualTo("Project/ImplicitSave"));
            Assert.That(provider.scope, Is.EqualTo(SettingsScope.Project));
        }

        [Test]
        public void SearchReachesEverySetting()
        {
            // Typing "backups" in the Project Settings search box should land on this page.
            var keywords = ImplicitSaveSettingsProvider.Create().keywords.ToList();

            foreach (var field in typeof(ImplicitSaveSettings).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                Assert.That(keywords, Has.Member(ObjectNames.NicifyVariableName(field.Name)));
            }
        }

        [Test]
        public void CreatingTheAsset_ReplacesTheDefaultsAlreadyInUse()
        {
            Assume.That(ImplicitSaveSettingsProvider.FindAssetInUse(), Is.Null,
                "This project already has a settings asset, so the test cannot start from the defaults.");

            var defaults = ImplicitSaveSettings.Instance;
            Assert.That(EditorUtility.IsPersistent(defaults), Is.False);

            var asset = ImplicitSaveSettingsProvider.CreateAsset(
                TempRoot + "/Resources/" + ImplicitSaveSettings.AssetName + ".asset");

            Assert.That(ImplicitSaveSettingsProvider.FindAssetInUse(), Is.SameAs(asset));
            Assert.That(ImplicitSaveSettings.Instance, Is.SameAs(asset),
                "The defaults read before the asset existed must not stay in use until the next domain reload.");
        }

        [Test]
        public void AnAssetOutsideResources_IsListedAsNotInUse()
        {
            var path = TempRoot + "/" + ImplicitSaveSettings.AssetName + ".asset";
            ImplicitSaveSettingsProvider.CreateAsset(path);

            var ignored = ImplicitSaveSettingsProvider.FindIgnoredAssetPaths(ImplicitSaveSettingsProvider.FindAssetInUse());

            Assert.That(ignored, Has.Member(path));
        }

        [Test]
        public void TheDefaults_RaiseNoWarnings()
        {
            var settings = ScriptableObject.CreateInstance<ImplicitSaveSettings>();
            try
            {
                Assert.That(ImplicitSaveSettingsProvider.FindProblems(settings), Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [Test]
        public void AZeroInterval_IsReportedOnlyWhileAutosaveIsOn()
        {
            var settings = ScriptableObject.CreateInstance<ImplicitSaveSettings>();
            try
            {
                settings.AutoSaveIntervalSeconds = 0f;
                Assert.That(ImplicitSaveSettingsProvider.FindProblems(settings), Has.Count.EqualTo(1));

                // With the tick off on purpose, the interval means nothing and is not worth a warning.
                settings.AutoSaveEnabled = false;
                Assert.That(ImplicitSaveSettingsProvider.FindProblems(settings), Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [TestCase("")]
        [TestCase("   ")]
        [TestCase("../outside")]
        [TestCase("saves/../..")]
        [TestCase("/absolute/saves")]
        public void AFolderNameThatLeavesThePersistentDataPath_IsReported(string folder)
        {
            var settings = ScriptableObject.CreateInstance<ImplicitSaveSettings>();
            try
            {
                settings.SaveFolderName = folder;
                Assert.That(ImplicitSaveSettingsProvider.FindProblems(settings), Has.Count.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }

        [TestCase("saves")]
        [TestCase("saves/v2")]
        [TestCase("my game saves")]
        public void APlainSubfolder_IsFine(string folder)
        {
            var settings = ScriptableObject.CreateInstance<ImplicitSaveSettings>();
            try
            {
                settings.SaveFolderName = folder;
                Assert.That(ImplicitSaveSettingsProvider.FindProblems(settings), Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(settings);
            }
        }
    }
}
