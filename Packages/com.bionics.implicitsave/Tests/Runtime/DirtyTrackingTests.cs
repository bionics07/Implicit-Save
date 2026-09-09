using System;
using ImplicitSave.Serialization;
using ImplicitSave.Storage;
using NUnit.Framework;
using UnityEngine;

namespace ImplicitSave.Tests
{
    /// <summary>
    /// Phase 5's first two criteria: an autosave tick that finds nothing new writes nothing, and a
    /// change made inside a collection is noticed.
    /// </summary>
    public class DirtyTrackingTests
    {
        private InMemorySaveStorage _storage;
        private ImplicitSaveSettings _settings;

        [SetUp]
        public void SetUp()
        {
            _storage = new InMemorySaveStorage();
            _settings = ScriptableObject.CreateInstance<ImplicitSaveSettings>();
            _settings.hideFlags = HideFlags.HideAndDontSave;
            ImplicitSaveSettings.Override(_settings);
            SaveManager.Configure(new NewtonsoftSaveSerializer(), _storage);
        }

        [TearDown]
        public void TearDown()
        {
            SaveManager.Shutdown();
            ImplicitSaveSettings.Override(null);
            UnityEngine.Object.DestroyImmediate(_settings);
        }

        [Test]
        public void ATickWithNoChanges_WritesNothing()
        {
            // The whole point of dirty tracking. A game that autosaves every 30 seconds for an hour
            // would otherwise write 120 identical files.
            SaveManager.Get<ItemsSaveData>().SelectedSlot = 1;
            SaveManager.AutoSave();
            SaveManager.FlushSynchronously(TimeSpan.FromSeconds(5));

            var afterFirst = _storage.WriteCount;
            Assert.That(afterFirst, Is.GreaterThan(0), "The first tick has something to write.");

            SaveManager.AutoSave();
            SaveManager.AutoSave();
            SaveManager.AutoSave();
            SaveManager.FlushSynchronously(TimeSpan.FromSeconds(5));

            Assert.That(_storage.WriteCount, Is.EqualTo(afterFirst),
                "Nothing changed, so nothing should have been written.");
        }

        [Test]
        public void AChangeToAField_IsNoticed()
        {
            var data = SaveManager.Get<ItemsSaveData>();
            SaveManager.AutoSave();
            SaveManager.FlushSynchronously(TimeSpan.FromSeconds(5));
            var before = _storage.WriteCount;

            data.SelectedSlot = 42;
            SaveManager.AutoSave();
            SaveManager.FlushSynchronously(TimeSpan.FromSeconds(5));

            Assert.That(_storage.WriteCount, Is.GreaterThan(before));
        }

        [Test]
        public void AChangeInsideASerializableDictionary_IsNoticed()
        {
            // This is the case a manual dirty flag and a generated property setter both miss, and
            // it is where most save mutations actually happen.
            var data = SaveManager.Get<InventorySaveData>();
            SaveManager.AutoSave();
            SaveManager.FlushSynchronously(TimeSpan.FromSeconds(5));
            var before = _storage.WriteCount;

            data.Counts["potion"] = 5;
            SaveManager.AutoSave();
            SaveManager.FlushSynchronously(TimeSpan.FromSeconds(5));

            Assert.That(_storage.WriteCount, Is.GreaterThan(before),
                "Nobody called MarkDirty here - the hash is what caught it.");
        }

        [Test]
        public void AChangeInsideAList_IsNoticed()
        {
            var data = SaveManager.Get<ItemsSaveData>();
            data.Owned.Add("corda");
            SaveManager.AutoSave();
            SaveManager.FlushSynchronously(TimeSpan.FromSeconds(5));
            var before = _storage.WriteCount;

            data.Owned[0] = "espada";
            SaveManager.AutoSave();
            SaveManager.FlushSynchronously(TimeSpan.FromSeconds(5));

            Assert.That(_storage.WriteCount, Is.GreaterThan(before));
        }

        [Test]
        public void AChangeInsideANestedObject_IsNoticed()
        {
            var data = SaveManager.Get<ItemsSaveData>();
            SaveManager.AutoSave();
            SaveManager.FlushSynchronously(TimeSpan.FromSeconds(5));
            var before = _storage.WriteCount;

            data.Stats.Strength = 12;
            SaveManager.AutoSave();
            SaveManager.FlushSynchronously(TimeSpan.FromSeconds(5));

            Assert.That(_storage.WriteCount, Is.GreaterThan(before));
        }

        [Test]
        public void ForceSave_WritesEvenWhenNothingChanged()
        {
            SaveManager.Get<ItemsSaveData>();
            SaveManager.ForceSave();
            var before = _storage.WriteCount;

            SaveManager.ForceSave();

            Assert.That(_storage.WriteCount, Is.GreaterThan(before));
        }

        [Test]
        public void ManualFlagStrategy_OnlyWritesWhenMarkDirtyWasCalled()
        {
            _settings.DirtyStrategy = DirtyStrategy.ManualFlag;

            var data = SaveManager.Get<ItemsSaveData>();
            data.SelectedSlot = 3;
            SaveManager.AutoSave();
            SaveManager.FlushSynchronously(TimeSpan.FromSeconds(5));

            Assert.That(_storage.WriteCount, Is.Zero,
                "This is the documented risk of ManualFlag: a real change silently not written.");

            data.MarkDirty();
            SaveManager.AutoSave();
            SaveManager.FlushSynchronously(TimeSpan.FromSeconds(5));

            Assert.That(_storage.WriteCount, Is.GreaterThan(0));
        }

        [Test]
        public void BothStrategy_CatchesAChangeNobodyFlagged()
        {
            _settings.DirtyStrategy = DirtyStrategy.Both;

            var data = SaveManager.Get<ItemsSaveData>();
            SaveManager.AutoSave();
            SaveManager.FlushSynchronously(TimeSpan.FromSeconds(5));
            var before = _storage.WriteCount;

            data.SelectedSlot = 7;
            SaveManager.AutoSave();
            SaveManager.FlushSynchronously(TimeSpan.FromSeconds(5));

            Assert.That(_storage.WriteCount, Is.GreaterThan(before),
                "Both keeps the hash as the safety net under the flag.");
        }

        [Test]
        public void AfterReload_TheNextWriteHappensEvenIfTheBytesMatch()
        {
            // The reloaded instance is a different object. Skipping its first write because the
            // bytes match would leave the tracker describing an object that no longer exists.
            SaveManager.Get<ItemsSaveData>().SelectedSlot = 4;
            SaveManager.ForceSave();
            SaveManager.Reload(0);

            var before = _storage.WriteCount;
            SaveManager.Get<ItemsSaveData>();
            SaveManager.AutoSave();
            SaveManager.FlushSynchronously(TimeSpan.FromSeconds(5));

            Assert.That(_storage.WriteCount, Is.GreaterThan(before));
        }

        [Test]
        public void TheHashIsStableForTheSameContent()
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes("{\"Counter\":1}");

            Assert.That(DirtyTracker.ComputeHash(bytes), Is.EqualTo(DirtyTracker.ComputeHash(bytes)));
        }

        [Test]
        public void TheHashChangesWithTheContent()
        {
            var a = System.Text.Encoding.UTF8.GetBytes("{\"Counter\":1}");
            var b = System.Text.Encoding.UTF8.GetBytes("{\"Counter\":2}");

            Assert.That(DirtyTracker.ComputeHash(a), Is.Not.EqualTo(DirtyTracker.ComputeHash(b)));
        }

        [TestCase(0)]
        [TestCase(1)]
        [TestCase(7)]
        [TestCase(8)]
        [TestCase(31)]
        [TestCase(32)]
        [TestCase(33)]
        [TestCase(500)]
        public void TheHashHandlesEveryLengthBoundary(int length)
        {
            // The 32, 8, 4 and 1 byte paths are all separate code in the algorithm.
            var bytes = new byte[length];
            for (var i = 0; i < length; i++)
            {
                bytes[i] = (byte)(i * 7);
            }

            var hash = DirtyTracker.ComputeHash(bytes);

            Assert.That(DirtyTracker.ComputeHash(bytes), Is.EqualTo(hash));

            if (length > 0)
            {
                bytes[length - 1] ^= 0xFF;
                Assert.That(DirtyTracker.ComputeHash(bytes), Is.Not.EqualTo(hash),
                    "A change in the last byte has to reach the hash.");
            }
        }

        [Test]
        public void AnUnchangedSave_StaysUnchangedAfterTheClockMoves()
        {
            // The bug this pins: the envelope carries $savedAt, stamped from the clock on every
            // serialization, and the dirty check hashed the whole file. Two passes over an untouched
            // object therefore differed and every tick wrote.
            //
            // It survived a full suite because the timestamp has ONE-SECOND resolution and tests run
            // in milliseconds - so this test has to let a second pass, and that cost is the point of
            // it. Without the wait it passes either way and proves nothing.
            var data = SaveManager.Get<PlayerProgressSaveData>();
            data.Currency.Gold = 10;
            SaveManager.Save<PlayerProgressSaveData>();
            SaveManager.FlushSynchronously(TimeSpan.FromSeconds(5));

            var afterFirstWrite = _storage.WriteCount;

            System.Threading.Thread.Sleep(1100);

            SaveManager.AutoSave();
            SaveManager.FlushSynchronously(TimeSpan.FromSeconds(5));

            Assert.That(_storage.WriteCount, Is.EqualTo(afterFirstWrite),
                "a tick over an untouched save must write nothing, however long it has been");
        }

        [Test]
        public void ARealChange_StillWritesAfterTheClockMoves()
        {
            // The other half: ignoring the timestamp must not make the check blind.
            var data = SaveManager.Get<PlayerProgressSaveData>();
            data.Currency.Gold = 10;
            SaveManager.Save<PlayerProgressSaveData>();
            SaveManager.FlushSynchronously(TimeSpan.FromSeconds(5));

            var afterFirstWrite = _storage.WriteCount;

            System.Threading.Thread.Sleep(1100);
            data.Currency.Gold = 11;

            SaveManager.AutoSave();
            SaveManager.FlushSynchronously(TimeSpan.FromSeconds(5));

            Assert.That(_storage.WriteCount, Is.EqualTo(afterFirstWrite + 1));
        }

        [Test]
        public void ContentHash_IgnoresTheTimestampAndNothingElse()
        {
            var serializer = new NewtonsoftSaveSerializer(prettyPrint: false);
            var data = new PlayerProgressSaveData();
            data.Currency.Gold = 7;

            var first = serializer.Serialize(data, "player_progress");
            System.Threading.Thread.Sleep(1100);
            var second = serializer.Serialize(data, "player_progress");

            Assert.That(first, Is.Not.EqualTo(second), "the raw bytes do differ - that is the trap");
            Assert.That(DirtyTracker.ComputeContentHash(first),
                Is.EqualTo(DirtyTracker.ComputeContentHash(second)));

            data.Currency.Gold = 8;
            var changed = serializer.Serialize(data, "player_progress");

            Assert.That(DirtyTracker.ComputeContentHash(changed),
                Is.Not.EqualTo(DirtyTracker.ComputeContentHash(first)));
        }

        [Test]
        public void ContentHash_FallsBackToHashingEverythingWithoutATimestamp()
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes("{\"data\":{\"Gold\":1}}");

            Assert.That(DirtyTracker.ComputeContentHash(bytes),
                Is.EqualTo(DirtyTracker.ComputeHash(bytes)));
        }
    }
}
