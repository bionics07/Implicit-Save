using System;
using System.Collections.Generic;
using System.Text;
using ImplicitSave.Serialization;
using ImplicitSave.Storage;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace ImplicitSave.Tests
{
    /// <summary>
    /// Phase 6: an old save is brought up to date step by step, a missing step is reported by name,
    /// and a save from a newer build is never overwritten.
    /// </summary>
    public class MigrationTests
    {
        private InMemorySaveStorage _storage;
        private SaveRepository _repository;

        [SetUp]
        public void SetUp()
        {
            _storage = new InMemorySaveStorage();
            _repository = new SaveRepository(
                new NewtonsoftSaveSerializer(),
                _storage,
                new MigrationPipeline(new ISaveMigration[]
                {
                    new PlayerProgressMigration1To2(),
                    new PlayerProgressMigration2To3()
                }));
        }

        [TearDown]
        public void TearDown()
        {
            LogAssert.ignoreFailingMessages = false;
        }

        [Test]
        public void AVersion1Save_IsMigratedAllTheWayToVersion3()
        {
            // The player who has not opened the game in two updates. Their file is v1 and the code
            // is v3; nobody wrote a 1-to-3 migration, and nobody should have to.
            WriteRawSave("player_progress", 1, "{\"Coins\": 250}");

            var data = _repository.Get<PlayerProgressSaveData>(0);

            Assert.That(data.Currency.Gold, Is.EqualTo(250), "The v1 Coins had to survive into v2's shape.");
            Assert.That(data.PlayerName, Is.EqualTo("Aventureiro"), "And then v3's step had to run on top.");
        }

        [Test]
        public void AVersion2Save_OnlyRunsTheRemainingStep()
        {
            WriteRawSave("player_progress", 2, "{\"Currency\": {\"Gold\": 40, \"Gems\": 7}}");

            var data = _repository.Get<PlayerProgressSaveData>(0);

            Assert.That(data.Currency.Gold, Is.EqualTo(40));
            Assert.That(data.Currency.Gems, Is.EqualTo(7), "A step that already ran must not run again.");
            Assert.That(data.PlayerName, Is.EqualTo("Aventureiro"));
        }

        [Test]
        public void ACurrentSave_IsNotTouched()
        {
            WriteRawSave("player_progress", 3, "{\"Currency\": {\"Gold\": 5, \"Gems\": 1}, \"PlayerName\": \"Ana\"}");

            var data = _repository.Get<PlayerProgressSaveData>(0);

            Assert.That(data.PlayerName, Is.EqualTo("Ana"), "No migration should have overwritten this.");
        }

        [Test]
        public void AMigratedSaveIsWrittenBackAtTheNewVersion()
        {
            WriteRawSave("player_progress", 1, "{\"Coins\": 99}");
            _repository.Get<PlayerProgressSaveData>(0);

            _repository.Save(typeof(PlayerProgressSaveData), 0);

            var json = Encoding.UTF8.GetString(_storage.Read(0, "player_progress"));
            Assert.That(json, Does.Contain("\"$schemaVersion\": 3"),
                "Once migrated, the file should not need migrating again next time.");
            Assert.That(json, Does.Not.Contain("Coins"), "The old field is gone for good.");
        }

        [Test]
        public void AMissingStep_NamesTheMigrationThatShouldExist()
        {
            // Silence would mean the save loads with default values and the player just finds their
            // progress gone.
            WriteRawSave("orphan_schema", 1, "{\"Value\": 5}");

            var failures = new List<SaveException>();
            _repository.Failed += failures.Add;
            LogAssert.ignoreFailingMessages = true;

            _repository.Get<UnmigratableSaveData>(0);

            Assert.That(failures, Has.Count.EqualTo(1));
            Assert.That(failures[0], Is.TypeOf<SaveCorruptedException>());
            Assert.That(failures[0].InnerException, Is.TypeOf<MigrationMissingException>());
            Assert.That(failures[0].InnerException.Message, Does.Contain("FromVersion 1"));
            Assert.That(failures[0].InnerException.Message, Does.Contain("ToVersion 2"),
                "The message has to say exactly which migration to write.");
        }

        [Test]
        public void AFutureVersionSave_IsNotOverwritten()
        {
            // A player who rolled back a build. The newer file holds progress this build cannot even
            // represent, so writing over it destroys it for good.
            WriteRawSave("player_progress", 99, "{\"Currency\": {\"Gold\": 7777}}");
            var before = _storage.Read(0, "player_progress");

            LogAssert.ignoreFailingMessages = true;
            var data = _repository.Get<PlayerProgressSaveData>(0);
            data.Currency.Gold = 1;
            _repository.Save(typeof(PlayerProgressSaveData), 0, force: true);

            Assert.That(_storage.Read(0, "player_progress"), Is.EqualTo(before),
                "The file must be byte-for-byte what it was.");
        }

        [Test]
        public void AFutureVersionSave_IsReportedAndMarkedReadOnly()
        {
            WriteRawSave("player_progress", 99, "{\"Currency\": {\"Gold\": 7777}}");

            var failures = new List<SaveException>();
            _repository.Failed += failures.Add;
            LogAssert.ignoreFailingMessages = true;

            var data = _repository.Get<PlayerProgressSaveData>(0);

            Assert.That(data.IsReadOnly, Is.True, "A game needs to be able to tell the player why.");
            Assert.That(failures, Has.Count.EqualTo(1));
            Assert.That(failures[0], Is.TypeOf<FutureVersionException>());

            var future = (FutureVersionException)failures[0];
            Assert.That(future.FileVersion, Is.EqualTo(99));
            Assert.That(future.SupportedVersion, Is.EqualTo(3));
        }

        [Test]
        public void AFutureVersionSave_IsNotDeserialized()
        {
            // Reading it into an object would drop every field this build does not know about, and
            // the next write would then persist that loss.
            WriteRawSave("player_progress", 99, "{\"Currency\": {\"Gold\": 7777}, \"PlayerName\": \"Ana\"}");

            LogAssert.ignoreFailingMessages = true;
            var data = _repository.Get<PlayerProgressSaveData>(0);

            Assert.That(data.Currency.Gold, Is.Zero, "Defaults, not the newer file's values.");
            Assert.That(data.PlayerName, Is.Empty);
        }

        [Test]
        public void AnUnreadableSave_FallsBackToTheBackup()
        {
            WriteRawSave("player_progress", 3, "{\"PlayerName\": \"do backup\"}");
            WriteRawSave("player_progress", 3, "{\"PlayerName\": \"atual\"}");
            _storage.Corrupt(0, "player_progress", Encoding.UTF8.GetBytes("{ truncado"));

            var data = _repository.Get<PlayerProgressSaveData>(0);

            Assert.That(data.PlayerName, Is.EqualTo("do backup"));
        }

        [Test]
        public void AnOldBackupIsMigratedToo()
        {
            // The backup is as old as the file it replaced, so recovery has to run the chain as well.
            WriteRawSave("player_progress", 1, "{\"Coins\": 500}");
            WriteRawSave("player_progress", 1, "{\"Coins\": 600}");
            _storage.Corrupt(0, "player_progress", Encoding.UTF8.GetBytes("{ truncado"));

            var data = _repository.Get<PlayerProgressSaveData>(0);

            Assert.That(data.Currency.Gold, Is.EqualTo(500),
                "Recovering from a backup must not skip the migration chain.");
        }

        [Test]
        public void AMigrationThatThrows_IsReportedWithItsName()
        {
            var pipeline = new MigrationPipeline(new ISaveMigration[] { new ThrowingMigration() });

            var exception = Assert.Throws<SaveException>(
                () => pipeline.Migrate(typeof(MigrationProbeSaveData), new Newtonsoft.Json.Linq.JObject(), 1, 2));

            Assert.That(exception.Message, Does.Contain("ThrowingMigration"));
        }

        [Test]
        public void AMigrationThatReturnsNull_IsReported()
        {
            var pipeline = new MigrationPipeline(new ISaveMigration[] { new NullReturningMigration() });

            var exception = Assert.Throws<SaveException>(
                () => pipeline.Migrate(typeof(MigrationProbeSaveData), new Newtonsoft.Json.Linq.JObject(), 2, 3));

            Assert.That(exception.Message, Does.Contain("has to return the payload"));
        }

        [Test]
        public void TheProjectsMigrationsAreFoundWithoutRegistration()
        {
            // Same promise as save types: declaring the class is the whole job.
            var found = SaveTypeRegistry.GetMigrations();
            var names = new List<string>();

            foreach (var migration in found)
            {
                names.Add(migration.GetType().Name);
            }

            Assert.That(names, Has.Member(nameof(PlayerProgressMigration1To2)));
            Assert.That(names, Has.Member(nameof(PlayerProgressMigration2To3)));
        }

        /// <summary>Writes a file at an exact schema version, bypassing the serializer.</summary>
        private void WriteRawSave(string saveId, int schemaVersion, string payload)
        {
            var json = "{\"$saveId\":\"" + saveId + "\",\"$schemaVersion\":" + schemaVersion +
                       ",\"$savedAt\":\"2026-01-01T00:00:00Z\",\"$appVersion\":\"1.0\",\"data\":" + payload + "}";

            _storage.Write(0, saveId, Encoding.UTF8.GetBytes(json));
        }

        private sealed class ThrowingMigration : ISaveMigration
        {
            public Type TargetType => typeof(MigrationProbeSaveData);
            public int FromVersion => 1;
            public int ToVersion => 2;

            public Newtonsoft.Json.Linq.JObject Migrate(Newtonsoft.Json.Linq.JObject data)
            {
                throw new InvalidOperationException("boom");
            }
        }

        private sealed class NullReturningMigration : ISaveMigration
        {
            // Um passo diferente do ThrowingMigration: duas migracoes do mesmo passo colidiriam no
            // registry do projeto, que enxerga ate classes privadas de teste.
            public Type TargetType => typeof(MigrationProbeSaveData);
            public int FromVersion => 2;
            public int ToVersion => 3;

            public Newtonsoft.Json.Linq.JObject Migrate(Newtonsoft.Json.Linq.JObject data)
            {
                return null;
            }
        }
    }
}
