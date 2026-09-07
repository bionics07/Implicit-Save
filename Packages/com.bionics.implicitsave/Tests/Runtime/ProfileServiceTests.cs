using System.Text;
using ImplicitSave.Storage;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace ImplicitSave.Tests
{
    /// <summary>
    /// Phase 3: several slots that keep their data apart, and an index that can be lost without
    /// taking the saves with it.
    /// </summary>
    public class ProfileServiceTests
    {
        private InMemorySaveStorage _storage;
        private ProfileService _profiles;

        [SetUp]
        public void SetUp()
        {
            _storage = new InMemorySaveStorage();
            _profiles = new ProfileService(_storage);
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void FirstRun_HasOneProfileReadyToUse()
        {
            // A game must have somewhere to save without the developer creating a slot first.
            Assert.That(_profiles.GetProfiles(), Has.Count.EqualTo(1));
            Assert.That(_profiles.ActiveProfileId, Is.Zero);
            Assert.That(_profiles.GetProfiles()[0].DisplayName, Is.EqualTo("Slot 1"));
        }

        [Test]
        public void Create_UsesTheLowestFreeId()
        {
            var second = _profiles.Create();
            var third = _profiles.Create();

            Assert.That(second.Id, Is.EqualTo(1));
            Assert.That(third.Id, Is.EqualTo(2));
        }

        [Test]
        public void Create_TakesADisplayName()
        {
            var profile = _profiles.Create("Partida da Ana");

            Assert.That(profile.DisplayName, Is.EqualTo("Partida da Ana"));
        }

        [Test]
        public void Create_RejectsAnIdThatIsTaken()
        {
            Assert.Throws<SaveException>(() => _profiles.Create(0));
        }

        [Test]
        public void Create_StampsTheCreationTime()
        {
            var profile = _profiles.Create();

            Assert.That(SaveTimestamp.TryParse(profile.CreatedAt, out _), Is.True);
        }

        [Test]
        public void SetActive_ChangesTheActiveProfileAndAnnouncesIt()
        {
            _profiles.Create();
            var changes = new System.Collections.Generic.List<string>();
            _profiles.ActiveProfileChanged += (from, to) => changes.Add($"{from}->{to}");

            _profiles.SetActive(1);

            Assert.That(_profiles.ActiveProfileId, Is.EqualTo(1));
            Assert.That(changes, Is.EqualTo(new[] { "0->1" }));
        }

        [Test]
        public void SetActive_OnTheCurrentProfile_DoesNothing()
        {
            var raised = 0;
            _profiles.ActiveProfileChanged += (from, to) => raised++;

            _profiles.SetActive(0);

            Assert.That(raised, Is.Zero);
        }

        [Test]
        public void SetActive_RejectsAProfileThatDoesNotExist()
        {
            Assert.Throws<SaveException>(() => _profiles.SetActive(7));
        }

        [Test]
        public void Delete_RemovesTheProfileAndItsSaves()
        {
            _profiles.Create();
            _storage.Write(1, "items", Encoding.UTF8.GetBytes("x"));

            _profiles.Delete(1);

            Assert.That(_profiles.Exists(1), Is.False);
            Assert.That(_storage.ListSaveIds(1), Is.Empty, "The saves have to go with the slot.");
        }

        [Test]
        public void Delete_OfTheActiveProfile_MovesActiveToAnotherOne()
        {
            _profiles.Create();
            _profiles.SetActive(1);

            _profiles.Delete(1);

            Assert.That(_profiles.ActiveProfileId, Is.Zero,
                "The active profile can never be one that no longer exists.");
        }

        [Test]
        public void Copy_DuplicatesEverySaveFile()
        {
            _storage.Write(0, "items", Encoding.UTF8.GetBytes("itens"));
            _storage.Write(0, "progress", Encoding.UTF8.GetBytes("progresso"));
            _profiles.Create();

            _profiles.Copy(0, 1);

            Assert.That(_storage.ListSaveIds(1), Is.EquivalentTo(new[] { "items", "progress" }));
            Assert.That(_storage.Read(1, "items"), Is.EqualTo(Encoding.UTF8.GetBytes("itens")));
        }

        [Test]
        public void Copy_ReplacesWhatWasInTheDestination()
        {
            _storage.Write(0, "items", Encoding.UTF8.GetBytes("novo"));
            _profiles.Create();
            _storage.Write(1, "outro", Encoding.UTF8.GetBytes("antigo"));

            _profiles.Copy(0, 1);

            Assert.That(_storage.ListSaveIds(1), Is.EqualTo(new[] { "items" }),
                "A half-old, half-new slot is a game state that never existed.");
        }

        [Test]
        public void Copy_CarriesPlayTimeAndCustomFields()
        {
            _storage.Write(0, "items", Encoding.UTF8.GetBytes("x"));
            _profiles.AddPlayTime(0, 120);
            _profiles.SetCustom(0, "level", "floresta_02");
            _profiles.Create();

            _profiles.Copy(0, 1);

            var destination = _profiles.Get(1);
            Assert.That(destination.PlayTimeSeconds, Is.EqualTo(120));
            Assert.That(destination.Custom["level"], Is.EqualTo("floresta_02"));
            Assert.That(destination.DisplayName, Is.EqualTo("Slot 2"),
                "The name stays the slot's own, or the player ends up with two slots alike.");
        }

        [Test]
        public void Copy_CreatesTheDestinationIfItIsNew()
        {
            _storage.Write(0, "items", Encoding.UTF8.GetBytes("x"));

            _profiles.Copy(0, 5);

            Assert.That(_profiles.Exists(5), Is.True);
        }

        [Test]
        public void Copy_RejectsASourceThatDoesNotExist()
        {
            Assert.Throws<SaveException>(() => _profiles.Copy(9, 0));
        }

        [Test]
        public void ThreeProfiles_KeepTheirDataApart()
        {
            _profiles.Create();
            _profiles.Create();
            _storage.Write(0, "items", Encoding.UTF8.GetBytes("zero"));
            _storage.Write(1, "items", Encoding.UTF8.GetBytes("um"));
            _storage.Write(2, "items", Encoding.UTF8.GetBytes("dois"));

            Assert.That(_storage.Read(0, "items"), Is.EqualTo(Encoding.UTF8.GetBytes("zero")));
            Assert.That(_storage.Read(1, "items"), Is.EqualTo(Encoding.UTF8.GetBytes("um")));
            Assert.That(_storage.Read(2, "items"), Is.EqualTo(Encoding.UTF8.GetBytes("dois")));
        }

        [Test]
        public void TheIndexSurvivesAReload()
        {
            _profiles.Create("Partida da Ana");
            _profiles.SetActive(1);
            _profiles.AddPlayTime(1, 55);

            _profiles.Reload();

            Assert.That(_profiles.ActiveProfileId, Is.EqualTo(1));
            Assert.That(_profiles.Get(1).DisplayName, Is.EqualTo("Partida da Ana"));
            Assert.That(_profiles.Get(1).PlayTimeSeconds, Is.EqualTo(55));
        }

        [Test]
        public void AnUnreadableIndex_IsRebuiltFromTheFoldersOnDisk()
        {
            // The index is a convenience. The saves on disk are the truth, and losing the index must
            // never mean losing them.
            _profiles.Create();
            _profiles.Create();
            _storage.Write(0, "items", Encoding.UTF8.GetBytes("a"));
            _storage.Write(1, "items", Encoding.UTF8.GetBytes("b"));
            _storage.Write(2, "items", Encoding.UTF8.GetBytes("c"));

            _storage.WriteIndex(Encoding.UTF8.GetBytes("{ isto nao e json valido"));
            _profiles.Reload();

            LogAssert.ignoreFailingMessages = true;
            var profiles = _profiles.GetProfiles();

            Assert.That(profiles, Has.Count.EqualTo(3), "Every slot with saves on disk has to come back.");
            Assert.That(_storage.Read(2, "items"), Is.EqualTo(Encoding.UTF8.GetBytes("c")));
        }

        [Test]
        public void AnUnreadableIndex_IsKeptRatherThanOverwritten()
        {
            _storage.Write(0, "items", Encoding.UTF8.GetBytes("a"));
            _storage.WriteIndex(Encoding.UTF8.GetBytes("{ corrompido"));
            _profiles.Reload();

            LogAssert.ignoreFailingMessages = true;
            _profiles.GetProfiles();

            Assert.That(_storage.Quarantined, Is.Not.Empty,
                "Even a broken index can be worth reading during a support ticket.");
        }

        [Test]
        public void AProfileFolderMissingFromTheIndex_IsAddedBack()
        {
            _profiles.Create();
            _storage.Write(3, "items", Encoding.UTF8.GetBytes("orfao"));

            _profiles.Reload();
            LogAssert.ignoreFailingMessages = true;

            Assert.That(_profiles.Exists(3), Is.True,
                "Saves on disk with no index entry are still the player's game.");
        }

        [Test]
        public void TheIndexIsWrittenInTheDocumentedShape()
        {
            _profiles.Create("Partida da Ana");
            _profiles.SetCustom(1, "level", "floresta_02");

            var json = Encoding.UTF8.GetString(_storage.ReadIndex());

            Assert.That(json, Does.Contain("\"activeProfile\""));
            Assert.That(json, Does.Contain("\"profiles\""));
            Assert.That(json, Does.Contain("\"displayName\": \"Partida da Ana\""));
            Assert.That(json, Does.Contain("\"playTimeSeconds\""));
            Assert.That(json, Does.Contain("\"level\": \"floresta_02\""),
                "The custom fields are what a load screen draws without opening a save.");
        }
    }
}
