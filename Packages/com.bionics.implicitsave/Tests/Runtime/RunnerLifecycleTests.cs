using System;
using System.Collections;
using ImplicitSave.Serialization;
using ImplicitSave.Storage;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ImplicitSave.Tests
{
    /// <summary>
    /// The hidden runner: exactly one of it, and the application hooks writing synchronously.
    /// </summary>
    public class RunnerLifecycleTests
    {
        private InMemorySaveStorage _storage;

        [SetUp]
        public void SetUp()
        {
            _storage = new InMemorySaveStorage();
            SaveManager.Configure(new NewtonsoftSaveSerializer(), _storage);
        }

        [TearDown]
        public void TearDown()
        {
            SaveManager.Shutdown();
        }

        [UnityTest]
        public IEnumerator ExactlyOneRunnerExists()
        {
            yield return null;

            var runners = Resources.FindObjectsOfTypeAll<ImplicitSaveRunner>();

            Assert.That(runners.Length, Is.EqualTo(1),
                "Two runners would double every write. With Disable Domain Reload on, the previous " +
                "session's statics survive and this is exactly where duplicates appear.");
        }

        [UnityTest]
        public IEnumerator TheRunnerIsHiddenFromTheHierarchy()
        {
            yield return null;

            var runner = Resources.FindObjectsOfTypeAll<ImplicitSaveRunner>()[0];

            Assert.That(runner.gameObject.hideFlags, Is.EqualTo(HideFlags.HideAndDontSave),
                "A game's hierarchy is the developer's, not ours.");
        }

        [UnityTest]
        public IEnumerator TheRunnerSurvivesASceneLoad()
        {
            yield return null;
            var before = Resources.FindObjectsOfTypeAll<ImplicitSaveRunner>()[0];

            yield return null;

            var after = Resources.FindObjectsOfTypeAll<ImplicitSaveRunner>();
            Assert.That(after.Length, Is.EqualTo(1));
            Assert.That(after[0], Is.SameAs(before));
        }

        [Test]
        public void FlushSynchronously_HasWrittenBeforeItReturns()
        {
            // This is the contract the pause hook depends on: after it returns, the OS can kill the
            // process and nothing is lost.
            SaveManager.Get<ItemsSaveData>().SelectedSlot = 5;

            SaveManager.FlushSynchronously(TimeSpan.FromSeconds(5));

            Assert.That(_storage.Exists(0, "items"), Is.True);
            Assert.That(SaveManager.Get<ItemsSaveData>(0).SelectedSlot, Is.EqualTo(5));
        }

        [Test]
        public void FlushSynchronously_WithNothingLoaded_IsHarmless()
        {
            Assert.DoesNotThrow(() => SaveManager.FlushSynchronously(TimeSpan.FromSeconds(5)));
        }

        [Test]
        public void FlushSynchronously_DrainsWhatTheTickQueued()
        {
            SaveManager.Get<ItemsSaveData>().SelectedSlot = 9;
            SaveManager.AutoSave();

            SaveManager.FlushSynchronously(TimeSpan.FromSeconds(5));

            Assert.That(_storage.Exists(0, "items"), Is.True,
                "A tick's queued write must not be left in flight when the app is going away.");
        }

        [Test]
        public void SettingsFallBackToDefaultsWithNoAsset()
        {
            // A project that never created the settings asset still has to work.
            var settings = ImplicitSaveSettings.Instance;

            Assert.That(settings, Is.Not.Null);
            Assert.That(settings.AutoSaveEnabled, Is.True);
            Assert.That(settings.DirtyStrategy, Is.EqualTo(DirtyStrategy.HashDiff));
            Assert.That(settings.AutoSaveIntervalSeconds, Is.GreaterThan(0f));
        }
    }
}
