using System.Collections.Generic;
using System.Text;
using ImplicitSave.Serialization;
using ImplicitSave.Storage;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace ImplicitSave.Tests
{
    /// <summary>
    /// Covers phase 1's second acceptance criterion: the whole system exercised against
    /// <see cref="InMemorySaveStorage"/>, without touching a disk.
    /// </summary>
    public class SaveRepositoryTests
    {
        private InMemorySaveStorage _storage;
        private SaveRepository _repository;

        [SetUp]
        public void SetUp()
        {
            _storage = new InMemorySaveStorage();
            _repository = new SaveRepository(new NewtonsoftSaveSerializer(), _storage);
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void Get_WithNoFile_ReturnsAFreshInstance()
        {
            var data = _repository.Get<ItemsSaveData>(0);

            Assert.That(data, Is.Not.Null);
            Assert.That(data.SelectedSlot, Is.Zero);
            Assert.That(_storage.WriteCount, Is.Zero, "Reading must not write anything on its own.");
        }

        [Test]
        public void Get_WithNoFile_ResetsToDefaultsButDoesNotReportALoad()
        {
            var data = _repository.Get<HookSaveData>(0);

            Assert.That(data.ResetCalls, Is.EqualTo(1));
            Assert.That(data.AfterLoadCalls, Is.Zero,
                "Nothing was loaded, so OnAfterLoad must not claim otherwise.");
        }

        [Test]
        public void Get_ReturnsTheSameInstanceOnEveryCall()
        {
            var first = _repository.Get<ItemsSaveData>(0);
            var second = _repository.Get<ItemsSaveData>(0);

            Assert.That(second, Is.SameAs(first),
                "One live instance per profile is what lets systems share state without a reference.");
        }

        [Test]
        public void Get_KeepsProfilesApart()
        {
            _repository.Get<ItemsSaveData>(0).SelectedSlot = 1;
            _repository.Get<ItemsSaveData>(1).SelectedSlot = 2;

            Assert.That(_repository.Get<ItemsSaveData>(0).SelectedSlot, Is.EqualTo(1));
            Assert.That(_repository.Get<ItemsSaveData>(1).SelectedSlot, Is.EqualTo(2));
        }

        [Test]
        public void Get_StampsTheProfileOnTheInstance()
        {
            Assert.That(_repository.Get<ItemsSaveData>(3).ProfileId, Is.EqualTo(3));
        }

        [Test]
        public void SaveThenReload_ReadsTheValuesBackFromStorage()
        {
            var data = _repository.Get<ItemsSaveData>(0);
            data.SelectedSlot = 9;
            data.Owned.Add("lantern");
            data.Stats.Strength = 4;

            _repository.Save<ItemsSaveData>(0);
            _repository.Reload(0);

            var restored = _repository.Get<ItemsSaveData>(0);
            Assert.That(restored, Is.Not.SameAs(data));
            Assert.That(restored.SelectedSlot, Is.EqualTo(9));
            Assert.That(restored.Owned, Is.EqualTo(new[] { "lantern" }));
            Assert.That(restored.Stats.Strength, Is.EqualTo(4));
        }

        [Test]
        public void Save_RunsTheBeforeSaveHook()
        {
            var data = _repository.Get<HookSaveData>(0);

            _repository.Save<HookSaveData>(0);

            Assert.That(data.BeforeSaveCalls, Is.EqualTo(1));
        }

        [Test]
        public void Load_RunsTheAfterLoadHook()
        {
            _repository.Get<HookSaveData>(0).Value = 5;
            _repository.Save<HookSaveData>(0);
            _repository.Reload(0);

            var restored = _repository.Get<HookSaveData>(0);

            Assert.That(restored.AfterLoadCalls, Is.EqualTo(1));
            Assert.That(restored.ResetCalls, Is.Zero, "A file existed, so defaults must not be applied.");
            Assert.That(restored.Value, Is.EqualTo(5));
        }

        [Test]
        public void Save_ClearsTheDirtyFlag()
        {
            var data = _repository.Get<ItemsSaveData>(0);
            data.MarkDirty();

            _repository.Save<ItemsSaveData>(0);

            Assert.That(data.IsDirty, Is.False);
        }

        [Test]
        public void Save_OnATypeNeverLoaded_DoesNothing()
        {
            _repository.Save<ItemsSaveData>(0);

            Assert.That(_storage.WriteCount, Is.Zero);
        }

        [Test]
        public void SaveAll_WritesOnlyTheRequestedProfile()
        {
            _repository.Get<ItemsSaveData>(0);
            _repository.Get<HookSaveData>(0);
            _repository.Get<ItemsSaveData>(1);

            _repository.SaveAll(0);

            Assert.That(_storage.ListSaveIds(0), Is.EquivalentTo(new[] { "items", "hooks" }));
            Assert.That(_storage.ListSaveIds(1), Is.Empty);
        }

        [Test]
        public void Reload_DiscardsUnsavedChanges()
        {
            _repository.Get<ItemsSaveData>(0).SelectedSlot = 8;

            _repository.Reload(0);

            Assert.That(_repository.Get<ItemsSaveData>(0).SelectedSlot, Is.Zero);
        }

        [Test]
        public void Reload_LeavesOtherProfilesLoaded()
        {
            var other = _repository.Get<ItemsSaveData>(1);

            _repository.Reload(0);

            Assert.That(_repository.Get<ItemsSaveData>(1), Is.SameAs(other));
        }

        [Test]
        public void Get_WithAnUnreadableFile_RecoversFromTheBackup()
        {
            var data = _repository.Get<ItemsSaveData>(0);
            data.SelectedSlot = 6;
            _repository.Save<ItemsSaveData>(0);   // becomes the backup
            data.SelectedSlot = 7;
            _repository.Save<ItemsSaveData>(0);   // current file

            _storage.Corrupt(0, "items", Encoding.UTF8.GetBytes("{ truncated"));
            _repository.Reload(0);

            // Recovery warns on its way through; only error logs would fail the test.
            Assert.That(_repository.Get<ItemsSaveData>(0).SelectedSlot, Is.EqualTo(6),
                "The backup held the previous write, so that is what recovery must return.");
        }

        [Test]
        public void Get_WithNoUsableFileOrBackup_QuarantinesAndStartsClean()
        {
            var failures = new List<SaveException>();
            _repository.Failed += failures.Add;

            _repository.Get<ItemsSaveData>(0).SelectedSlot = 5;
            _repository.Save<ItemsSaveData>(0);
            _storage.Corrupt(0, "items", Encoding.UTF8.GetBytes("{ truncated"));
            _repository.Reload(0);

            // Giving up on a save is reported as an error, which would otherwise fail the test.
            LogAssert.ignoreFailingMessages = true;
            var recovered = _repository.Get<ItemsSaveData>(0);

            Assert.That(recovered.SelectedSlot, Is.Zero, "A fresh instance is the only safe fallback.");
            Assert.That(failures, Has.Count.EqualTo(1));
            Assert.That(failures[0], Is.TypeOf<SaveCorruptedException>());
            Assert.That(_storage.Quarantined, Is.Not.Empty,
                "The unreadable file must be kept - it may be the player's only copy.");
        }

        [Test]
        public void IsLoaded_ReflectsWhatIsInMemory()
        {
            Assert.That(_repository.IsLoaded(typeof(ItemsSaveData), 0), Is.False);

            _repository.Get<ItemsSaveData>(0);

            Assert.That(_repository.IsLoaded(typeof(ItemsSaveData), 0), Is.True);
        }
    }
}
