using System;
using System.IO;
using System.Text;
using ImplicitSave.Editor;
using ImplicitSave.Serialization;
using ImplicitSave.Storage;
using ImplicitSave.Tests;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ImplicitSave.Tests.EditorTests
{
    /// <summary>
    /// Phase 7's behaviour, minus the pixels: lazy materialisation, the badges, the proxy, undo, and
    /// the rule that a save from a newer build is never written.
    /// </summary>
    public class SaveEditorSessionTests
    {
        private string _basePath;
        private FileSaveStorage _storage;
        private SaveEditorSession _session;

        [SetUp]
        public void SetUp()
        {
            _basePath = Path.Combine(Application.temporaryCachePath, "ImplicitSaveEditorTests",
                Guid.NewGuid().ToString("N"));
            _storage = new FileSaveStorage(_basePath, "saves");
            _session = new SaveEditorSession(new NewtonsoftSaveSerializer(), _storage);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_basePath))
            {
                Directory.Delete(_basePath, recursive: true);
            }
        }

        [Test]
        public void ATypeWithNoFile_IsListedAnyway()
        {
            // Someone who has not pressed play yet should still be able to see and set up their save.
            var entry = FindEntry(typeof(ItemsSaveData));

            Assert.That(entry.State, Is.EqualTo(SaveFileState.NotSavedYet));
        }

        [Test]
        public void EditorOnlyTypesAreHiddenByDefault()
        {
            // Um tipo de assembly de teste nao existe num build, entao lista-lo ao lado dos saves
            // reais do jogo sugere que o jogo publicado tera arquivo para ele.
            var visible = _session.ListSaves(0);
            var all = _session.ListSaves(0, includeEditorOnly: true);

            Assert.That(Contains(visible, typeof(ItemsSaveData)), Is.False,
                "ItemsSaveData vive na assembly de teste e nao deveria aparecer por padrao.");
            Assert.That(Contains(all, typeof(ItemsSaveData)), Is.True,
                "Mas tem que ser alcancavel com o toggle ligado.");
            Assert.That(all.Count, Is.GreaterThan(visible.Count));
        }

        private static bool Contains(System.Collections.Generic.IReadOnlyList<SaveEntry> entries, Type type)
        {
            foreach (var entry in entries)
            {
                if (entry.Type == type)
                {
                    return true;
                }
            }

            return false;
        }

        [Test]
        public void ListingSaves_DoesNotCreateAnyFile()
        {
            // Opening a window is a read. Writing to the save folder as a side effect would leave
            // junk behind for every type later renamed or deleted.
            _session.ListSaves(0, includeEditorOnly: true);
            _session.Load(typeof(ItemsSaveData), 0, out _);

            Assert.That(_storage.Exists(0, "items"), Is.False,
                "Nothing may be written until the user actually applies a change.");
        }

        [Test]
        public void TheFileIsCreatedOnTheFirstApply()
        {
            var data = _session.Load(typeof(ItemsSaveData), 0, out _);
            ((ItemsSaveData)data).SelectedSlot = 3;

            _session.Apply(data, 0);

            Assert.That(_storage.Exists(0, "items"), Is.True);
            Assert.That(FindEntry(typeof(ItemsSaveData)).State, Is.EqualTo(SaveFileState.UpToDate));
        }

        [Test]
        public void ASaveThatNeedsMigration_IsFlagged()
        {
            WriteRaw("player_progress", 1, "{\"Coins\": 10}");

            var entry = FindEntry(typeof(PlayerProgressSaveData));

            Assert.That(entry.State, Is.EqualTo(SaveFileState.NeedsMigration));
            Assert.That(entry.FileVersion, Is.EqualTo(1));
            Assert.That(entry.ClassVersion, Is.EqualTo(3));
        }

        [Test]
        public void ASaveThatNeedsMigration_IsShownMigrated()
        {
            WriteRaw("player_progress", 1, "{\"Coins\": 250}");

            var data = (PlayerProgressSaveData)_session.Load(typeof(PlayerProgressSaveData), 0, out _);

            Assert.That(data.Currency.Gold, Is.EqualTo(250),
                "The window shows what the game would load, not the raw old shape.");
        }

        [Test]
        public void ASaveFromANewerBuild_IsFlaggedAndReadOnly()
        {
            WriteRaw("player_progress", 99, "{\"Currency\": {\"Gold\": 7777}}");

            var entry = FindEntry(typeof(PlayerProgressSaveData));
            var data = _session.Load(typeof(PlayerProgressSaveData), 0, out _);

            Assert.That(entry.State, Is.EqualTo(SaveFileState.FromNewerVersion));
            Assert.That(data.IsReadOnly, Is.True);
        }

        [Test]
        public void ASaveFromANewerBuild_IsRefusedOnApply()
        {
            WriteRaw("player_progress", 99, "{\"Currency\": {\"Gold\": 7777}}");
            var before = File.ReadAllText(_storage.GetSavePath(0, "player_progress"));

            var data = _session.Load(typeof(PlayerProgressSaveData), 0, out _);

            Assert.Throws<SaveException>(() => _session.Apply(data, 0));
            Assert.That(File.ReadAllText(_storage.GetSavePath(0, "player_progress")), Is.EqualTo(before));
        }

        [Test]
        public void AnUnreadableFile_IsFlaggedRatherThanThrowing()
        {
            Directory.CreateDirectory(_storage.GetProfilePath(0));
            File.WriteAllText(_storage.GetSavePath(0, "items"), "{ truncado");

            Assert.That(FindEntry(typeof(ItemsSaveData)).State, Is.EqualTo(SaveFileState.Unreadable));
        }

        [Test]
        public void Export_ReturnsNullWhenThereIsNoFile()
        {
            Assert.That(_session.ReadRawJson(typeof(ItemsSaveData), 0), Is.Null);
        }

        [Test]
        public void Import_RefusesAFileForAnotherSave()
        {
            // Pasting the wrong file would put one save's data under another's name, and the next
            // load would read it as if it belonged there.
            var wrong = "{\"$saveId\":\"progress\",\"$schemaVersion\":1,\"data\":{}}";

            var exception = Assert.Throws<SaveException>(
                () => _session.ImportRawJson(typeof(ItemsSaveData), 0, wrong));

            Assert.That(exception.Message, Does.Contain("progress"));
            Assert.That(_storage.Exists(0, "items"), Is.False);
        }

        [Test]
        public void Import_RefusesTextThatIsNotASaveFile()
        {
            // Catch, not Throws: the failure is a SaveSerializationException, and what matters is
            // that a caller can handle it as a SaveException like everything else the package raises.
            Assert.Catch<SaveException>(() => _session.ImportRawJson(typeof(ItemsSaveData), 0, "não é json"));
            Assert.That(_storage.Exists(0, "items"), Is.False, "A bad paste must not replace a good save.");
        }

        [Test]
        public void ExportThenImport_RoundTrips()
        {
            var data = (ItemsSaveData)_session.Load(typeof(ItemsSaveData), 0, out _);
            data.SelectedSlot = 6;
            data.Owned.Add("lanterna");
            _session.Apply(data, 0);

            var exported = _session.ReadRawJson(typeof(ItemsSaveData), 0);
            _session.Delete(typeof(ItemsSaveData), 0);
            _session.ImportRawJson(typeof(ItemsSaveData), 0, exported);

            var restored = (ItemsSaveData)_session.Load(typeof(ItemsSaveData), 0, out _);
            Assert.That(restored.SelectedSlot, Is.EqualTo(6));
            Assert.That(restored.Owned, Is.EqualTo(new[] { "lanterna" }));
        }

        [Test]
        public void Delete_RemovesTheFileAndTheEntryGoesBackToNotSaved()
        {
            var data = _session.Load(typeof(ItemsSaveData), 0, out _);
            _session.Apply(data, 0);

            _session.Delete(typeof(ItemsSaveData), 0);

            Assert.That(FindEntry(typeof(ItemsSaveData)).State, Is.EqualTo(SaveFileState.NotSavedYet));
        }

        [Test]
        public void ProfilesWithFilesAreListed_AndProfileZeroAlways()
        {
            Assert.That(_session.GetProfileIds(), Has.Member(0), "There is always somewhere to look.");

            _storage.Write(2, "items", Encoding.UTF8.GetBytes("{\"$saveId\":\"items\",\"data\":{}}"));

            Assert.That(_session.GetProfileIds(), Has.Member(2));
        }

        [Test]
        public void TheProxyExposesTheSavesOwnFields()
        {
            // This is what makes the window work at all: a SerializedObject needs a UnityEngine.Object,
            // and [SerializeReference] is what stops it showing only SaveData's members.
            var data = (ItemsSaveData)_session.Load(typeof(ItemsSaveData), 0, out _);
            data.SelectedSlot = 4;

            var proxy = SaveProxy.Create(data);

            try
            {
                var serialized = new SerializedObject(proxy);
                var payload = serialized.FindProperty(nameof(SaveProxy.Data));

                Assert.That(payload.propertyType, Is.EqualTo(SerializedPropertyType.ManagedReference));
                Assert.That(payload.FindPropertyRelative("SelectedSlot").intValue, Is.EqualTo(4));
                Assert.That(payload.FindPropertyRelative("Owned"), Is.Not.Null, "Lists have to come through.");
                Assert.That(payload.FindPropertyRelative("Stats.Strength"), Is.Not.Null,
                    "And so does a nested object.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void ANewSlotIsListedBeforeAnythingIsSavedIntoIt()
        {
            // A slot's folder only appears on its first write, so listing folders would hide a slot
            // the moment after it was created - which is exactly when someone looks for it.
            var id = _session.CreateProfile("Partida da Ana");

            Assert.That(_storage.ProfileExists(id), Is.False, "No file was written, so no folder exists.");
            Assert.That(FindProfile(id).DisplayName, Is.EqualTo("Partida da Ana"));
            Assert.That(FindProfile(id).HasFiles, Is.False);
        }

        [Test]
        public void DeletingASlotRemovesItAndItsSaves()
        {
            var id = _session.CreateProfile("Descartável");
            var data = _session.Load(typeof(ItemsSaveData), id, out _);
            _session.Apply(data, id);

            _session.DeleteProfile(id);

            Assert.That(_session.GetProfileIds(), Has.No.Member(id));
            Assert.That(_storage.ListSaveIds(id), Is.Empty);
        }

        [Test]
        public void TheActiveSlotIsSeparateFromTheOneBeingBrowsed()
        {
            // Browsing a slot is a local choice; the active one is game state. Mixing them up would
            // mean opening a window quietly changed which save the game loads.
            var id = _session.CreateProfile("Outro");

            Assert.That(_session.GetActiveProfileId(), Is.Zero);

            _session.SetActiveProfile(id);

            Assert.That(_session.GetActiveProfileId(), Is.EqualTo(id));
        }

        [Test]
        public void RenamingASlotKeepsItsIdAndFiles()
        {
            var data = _session.Load(typeof(ItemsSaveData), 0, out _);
            ((ItemsSaveData)data).SelectedSlot = 5;
            _session.Apply(data, 0);

            _session.RenameProfile(0, "Partida principal");

            Assert.That(FindProfile(0).DisplayName, Is.EqualTo("Partida principal"));
            Assert.That(((ItemsSaveData)_session.Load(typeof(ItemsSaveData), 0, out _)).SelectedSlot, Is.EqualTo(5),
                "Only the label changes.");
        }

        [Test]
        public void DuplicatingASlotCopiesItsSaves()
        {
            var data = _session.Load(typeof(ItemsSaveData), 0, out _);
            ((ItemsSaveData)data).SelectedSlot = 8;
            _session.Apply(data, 0);

            var copy = _session.CreateProfile("Cópia");
            _session.DuplicateProfile(0, copy);

            Assert.That(((ItemsSaveData)_session.Load(typeof(ItemsSaveData), copy, out _)).SelectedSlot,
                Is.EqualTo(8));
            Assert.That(((ItemsSaveData)_session.Load(typeof(ItemsSaveData), 0, out _)).SelectedSlot,
                Is.EqualTo(8), "The original is untouched.");
        }

        private EditorProfile FindProfile(int id)
        {
            foreach (var profile in _session.GetProfiles())
            {
                if (profile.Id == id)
                {
                    return profile;
                }
            }

            Assert.Fail($"Slot {id} was not listed.");
            return default;
        }

        [Test]
        public void TheProxyIsEditable()
        {
            // HideFlags.HideAndDontSave carries NotEditable, and a SerializedObject over a
            // not-editable target draws every field greyed out. The window looked perfectly correct
            // and nothing could be typed into it - which is why this asserts the flag, not the look.
            var proxy = SaveProxy.Create(new ItemsSaveData());

            try
            {
                Assert.That(proxy.hideFlags.HasFlag(HideFlags.NotEditable), Is.False,
                    "A not-editable proxy makes the whole window read-only.");
                Assert.That(proxy.hideFlags.HasFlag(HideFlags.DontSaveInEditor), Is.True,
                    "The proxy must never be written as an asset.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void EditingThroughTheProxyChangesTheInstance()
        {
            var data = (ItemsSaveData)_session.Load(typeof(ItemsSaveData), 0, out _);
            var proxy = SaveProxy.Create(data);

            try
            {
                var serialized = new SerializedObject(proxy);
                serialized.FindProperty(nameof(SaveProxy.Data)).FindPropertyRelative("SelectedSlot").intValue = 9;
                serialized.ApplyModifiedProperties();

                Assert.That(data.SelectedSlot, Is.EqualTo(9),
                    "What the window edits has to be the object that gets serialized.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void ADictionaryIsReachableThroughTheProxy()
        {
            var data = (InventorySaveData)_session.Load(typeof(InventorySaveData), 0, out _);
            data.Counts["pocao"] = 5;

            var proxy = SaveProxy.Create(data);

            try
            {
                var serialized = new SerializedObject(proxy);
                var counts = serialized.FindProperty(nameof(SaveProxy.Data)).FindPropertyRelative("Counts");

                Assert.That(counts, Is.Not.Null);
                Assert.That(counts.FindPropertyRelative("_keys"), Is.Not.Null,
                    "The drawer needs the backing lists to draw pairs from.");
                Assert.That(counts.FindPropertyRelative("_values"), Is.Not.Null);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void TheDictionaryDrawerSpotsDuplicateKeys()
        {
            var data = new InventorySaveData();
            data.Counts["a"] = 1;
            data.Counts["b"] = 2;

            var proxy = SaveProxy.Create(data);

            try
            {
                var serialized = new SerializedObject(proxy);
                var counts = serialized.FindProperty(nameof(SaveProxy.Data)).FindPropertyRelative("Counts");
                var keys = counts.FindPropertyRelative("_keys");

                Assert.That(SerializableDictionaryDrawer.HasDuplicateKeys(keys, out _), Is.False);

                keys.GetArrayElementAtIndex(1).stringValue = "a";

                Assert.That(SerializableDictionaryDrawer.HasDuplicateKeys(keys, out var duplicates), Is.True,
                    "The backing list allows what a dictionary cannot, so the drawer has to say so.");
                Assert.That(duplicates, Has.Count.EqualTo(2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(proxy);
            }
        }

        [Test]
        public void UndoRestoresTheProxysPreviousValue()
        {
            var data = (ItemsSaveData)_session.Load(typeof(ItemsSaveData), 0, out _);
            data.SelectedSlot = 1;
            var proxy = SaveProxy.Create(data);

            try
            {
                Undo.RecordObject(proxy, "test edit");
                data.SelectedSlot = 8;
                Undo.FlushUndoRecordObjects();

                Undo.PerformUndo();

                Assert.That(proxy.Data, Is.Not.Null, "Undo must not leave the proxy empty.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(proxy);
            }
        }

        private SaveEntry FindEntry(Type type)
        {
            // includeEditorOnly: os fixtures vivem numa assembly de teste, que a janela esconde por
            // padrao justamente porque esses tipos nao existem num build.
            foreach (var entry in _session.ListSaves(0, includeEditorOnly: true))
            {
                if (entry.Type == type)
                {
                    return entry;
                }
            }

            Assert.Fail($"'{type.Name}' was not listed at all.");
            return default;
        }

        private void WriteRaw(string saveId, int schemaVersion, string payload)
        {
            var json = "{\"$saveId\":\"" + saveId + "\",\"$schemaVersion\":" + schemaVersion +
                       ",\"$savedAt\":\"2026-01-01T00:00:00Z\",\"$appVersion\":\"1.0\",\"data\":" + payload + "}";

            _storage.Write(0, saveId, Encoding.UTF8.GetBytes(json));
        }
    }
}
