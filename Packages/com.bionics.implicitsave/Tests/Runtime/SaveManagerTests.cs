using System;
using System.Collections.Generic;
using ImplicitSave.Serialization;
using ImplicitSave.Storage;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace ImplicitSave.Tests
{
    /// <summary>
    /// The facade a game actually calls, and the one behaviour that needs both halves of the system
    /// at once: switching profile must not lose what is in memory.
    /// </summary>
    public class SaveManagerTests
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
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void Get_WorksWithNoSetupBeyondDeclaringTheClass()
        {
            var items = SaveManager.Get<ItemsSaveData>();

            Assert.That(items, Is.Not.Null);
            Assert.That(SaveManager.ActiveProfileId, Is.Zero);
        }

        [Test]
        public void Get_ReadsAndWritesTheActiveProfile()
        {
            SaveManager.CreateProfile();
            SaveManager.Get<ItemsSaveData>().SelectedSlot = 1;
            SaveManager.SaveAll();

            SaveManager.SetActiveProfile(1);
            SaveManager.Get<ItemsSaveData>().SelectedSlot = 2;
            SaveManager.SaveAll();

            Assert.That(SaveManager.Get<ItemsSaveData>(0).SelectedSlot, Is.EqualTo(1));
            Assert.That(SaveManager.Get<ItemsSaveData>(1).SelectedSlot, Is.EqualTo(2));
        }

        [Test]
        public void SetActiveProfile_WritesTheOldProfileBeforeSwitching()
        {
            // Without this, everything changed since the last save is gone the moment the player
            // picks another slot.
            SaveManager.CreateProfile();
            SaveManager.Get<ItemsSaveData>().SelectedSlot = 9;

            SaveManager.SetActiveProfile(1);
            SaveManager.SetActiveProfile(0);

            Assert.That(SaveManager.Get<ItemsSaveData>().SelectedSlot, Is.EqualTo(9),
                "The unsaved change had to be flushed on the way out.");
        }

        [Test]
        public void SetActiveProfile_KeepsWhatIsAlreadyLoaded()
        {
            // A live instance is shared: Get<T>(1) and Get<T>() after switching to 1 are the same
            // object. Dropping it because the active profile moved would throw away changes the
            // game made on purpose.
            SaveManager.CreateProfile();
            var loaded = SaveManager.Get<ItemsSaveData>(1);
            loaded.SelectedSlot = 99;

            SaveManager.SetActiveProfile(1);

            Assert.That(SaveManager.Get<ItemsSaveData>(), Is.SameAs(loaded));
            Assert.That(SaveManager.Get<ItemsSaveData>().SelectedSlot, Is.EqualTo(99));
        }

        [Test]
        public void Reload_IsHowYouAskForAFreshReadFromStorage()
        {
            SaveManager.CreateProfile();
            SaveManager.Get<ItemsSaveData>(1).SelectedSlot = 4;
            SaveManager.Save<ItemsSaveData>(1);
            SaveManager.Get<ItemsSaveData>(1).SelectedSlot = 99;

            SaveManager.Reload(1);

            Assert.That(SaveManager.Get<ItemsSaveData>(1).SelectedSlot, Is.EqualTo(4),
                "Discarding unwritten changes is explicit, never a side effect of switching slots.");
        }

        [Test]
        public void SetActiveProfile_AnnouncesTheChange()
        {
            SaveManager.CreateProfile();
            var changes = new List<string>();
            SaveManager.ActiveProfileChanged += (from, to) => changes.Add($"{from}->{to}");

            SaveManager.SetActiveProfile(1);

            Assert.That(changes, Is.EqualTo(new[] { "0->1" }));
        }

        [Test]
        public void Saved_IsRaisedWithTheTypeAndProfile()
        {
            var saved = new List<string>();
            SaveManager.Saved += (type, profile) => saved.Add($"{type.Name}@{profile}");

            SaveManager.Get<ItemsSaveData>();
            SaveManager.Save<ItemsSaveData>();

            Assert.That(saved, Is.EqualTo(new[] { "ItemsSaveData@0" }));
        }

        [Test]
        public void Loaded_IsRaisedOnlyWhenSomethingWasRead()
        {
            var loaded = new List<string>();
            SaveManager.Loaded += (type, profile) => loaded.Add($"{type.Name}@{profile}");

            SaveManager.Get<ItemsSaveData>();
            Assert.That(loaded, Is.Empty, "Nothing was on disk, so nothing was loaded.");

            SaveManager.Save<ItemsSaveData>();
            SaveManager.Reload(0);
            SaveManager.Get<ItemsSaveData>();

            Assert.That(loaded, Is.EqualTo(new[] { "ItemsSaveData@0" }));
        }

        [Test]
        public void DeleteProfile_DropsWhatWasInMemoryToo()
        {
            SaveManager.CreateProfile();
            SaveManager.Get<ItemsSaveData>(1).SelectedSlot = 3;
            SaveManager.Save<ItemsSaveData>(1);

            SaveManager.DeleteProfile(1);

            Assert.That(SaveManager.ProfileExists(1), Is.False);
            Assert.That(_storage.ListSaveIds(1), Is.Empty,
                "A deleted slot must not linger in memory and get written back later.");
        }

        [Test]
        public void CopyProfile_FlushesTheSourceFirst()
        {
            SaveManager.Get<ItemsSaveData>().SelectedSlot = 7;

            SaveManager.CopyProfile(0, 1);

            Assert.That(SaveManager.Get<ItemsSaveData>(1).SelectedSlot, Is.EqualTo(7),
                "Copying a slot has to copy what the player sees, not the last thing written.");
        }

        [Test]
        public void GetProfiles_ListsTheSlotsForALoadScreen()
        {
            SaveManager.CreateProfile("Partida da Ana");

            var profiles = SaveManager.GetProfiles();

            Assert.That(profiles, Has.Count.EqualTo(2));
            Assert.That(profiles[1].DisplayName, Is.EqualTo("Partida da Ana"));
        }

        [Test]
        public void Failed_ForwardsTheRepositoryFailure()
        {
            var failures = new List<SaveException>();
            SaveManager.Failed += failures.Add;

            SaveManager.Get<ItemsSaveData>();
            SaveManager.Save<ItemsSaveData>();
            _storage.Corrupt(0, "items", System.Text.Encoding.UTF8.GetBytes("{ truncado"));
            SaveManager.Reload(0);

            LogAssert.ignoreFailingMessages = true;
            SaveManager.Get<ItemsSaveData>();

            Assert.That(failures, Has.Count.EqualTo(1));
            Assert.That(failures[0], Is.TypeOf<SaveCorruptedException>());
        }

        [Test]
        public void Shutdown_ForgetsTheWiring()
        {
            SaveManager.Get<ItemsSaveData>().SelectedSlot = 5;
            SaveManager.SaveAll();

            SaveManager.Shutdown();
            SaveManager.Configure(new NewtonsoftSaveSerializer(), new InMemorySaveStorage());

            Assert.That(SaveManager.Get<ItemsSaveData>().SelectedSlot, Is.Zero,
                "New storage, nothing carried over.");
        }
    }
}
