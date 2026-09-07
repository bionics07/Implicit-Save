using System;
using System.IO;
using System.Text;
using ImplicitSave.Storage;
using NUnit.Framework;
using UnityEngine;

namespace ImplicitSave.Tests
{
    /// <summary>
    /// Covers phase 1's third acceptance criterion: a write that dies halfway must not leave a
    /// corrupted file behind.
    /// </summary>
    /// <remarks>
    /// These are the only tests that touch a disk, and they do it in a throwaway folder. Everything
    /// else runs against <see cref="InMemorySaveStorage"/>.
    /// </remarks>
    public class FileSaveStorageTests
    {
        private string _basePath;
        private FileSaveStorage _storage;

        [SetUp]
        public void SetUp()
        {
            _basePath = Path.Combine(Application.temporaryCachePath, "ImplicitSaveTests", Guid.NewGuid().ToString("N"));
            _storage = new FileSaveStorage(_basePath, "saves");
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
        public void Write_ThenRead_ReturnsTheSameBytes()
        {
            var content = Encoding.UTF8.GetBytes("{\"$saveId\":\"items\"}");

            _storage.Write(0, "items", content);

            Assert.That(_storage.Exists(0, "items"), Is.True);
            Assert.That(_storage.Read(0, "items"), Is.EqualTo(content));
        }

        [Test]
        public void Write_PutsTheFileUnderTheProfileFolder()
        {
            _storage.Write(2, "items", Encoding.UTF8.GetBytes("x"));

            var expected = Path.Combine(_basePath, "saves", "2", "items.json");
            Assert.That(File.Exists(expected), Is.True, $"Expected the save at '{expected}'.");
        }

        [Test]
        public void Write_KeepsThePreviousContentAsBackup()
        {
            _storage.Write(0, "items", Encoding.UTF8.GetBytes("first"));
            _storage.Write(0, "items", Encoding.UTF8.GetBytes("second"));

            Assert.That(_storage.Read(0, "items"), Is.EqualTo(Encoding.UTF8.GetBytes("second")));
            Assert.That(_storage.TryReadBackup(0, "items", out var backup), Is.True);
            Assert.That(backup, Is.EqualTo(Encoding.UTF8.GetBytes("first")));
        }

        [Test]
        public void Write_FailingAfterTheTempFile_LeavesThePreviousSaveIntact()
        {
            _storage.Write(0, "items", Encoding.UTF8.GetBytes("the good save"));

            // Die exactly where it hurts: the new payload is on disk, the swap has not happened.
            _storage.AfterTempWritten = _ => throw new IOException("simulated power loss");

            Assert.Throws<SaveStorageException>(
                () => _storage.Write(0, "items", Encoding.UTF8.GetBytes("the doomed write")));

            _storage.AfterTempWritten = null;

            Assert.That(_storage.Read(0, "items"), Is.EqualTo(Encoding.UTF8.GetBytes("the good save")),
                "A failed write must never be visible in the real file.");
        }

        [Test]
        public void Write_FailingAfterTheTempFile_DoesNotLeaveTheTempFileBehind()
        {
            _storage.AfterTempWritten = _ => throw new IOException("simulated power loss");

            Assert.Throws<SaveStorageException>(() => _storage.Write(0, "items", Encoding.UTF8.GetBytes("doomed")));
            _storage.AfterTempWritten = null;

            var tempPath = _storage.GetSavePath(0, "items") + ".tmp";
            Assert.That(File.Exists(tempPath), Is.False, "A failed write must clean up after itself.");
        }

        [Test]
        public void Write_FailingOnTheFirstEverWrite_LeavesNoFileAtAll()
        {
            _storage.AfterTempWritten = _ => throw new IOException("simulated power loss");

            Assert.Throws<SaveStorageException>(() => _storage.Write(0, "items", Encoding.UTF8.GetBytes("doomed")));
            _storage.AfterTempWritten = null;

            Assert.That(_storage.Exists(0, "items"), Is.False,
                "Half a first save is worse than no save - the reader would treat it as real.");
        }

        [Test]
        public void Write_AfterAFailedAttempt_Succeeds()
        {
            _storage.Write(0, "items", Encoding.UTF8.GetBytes("first"));

            _storage.AfterTempWritten = _ => throw new IOException("simulated power loss");
            Assert.Throws<SaveStorageException>(() => _storage.Write(0, "items", Encoding.UTF8.GetBytes("doomed")));
            _storage.AfterTempWritten = null;

            _storage.Write(0, "items", Encoding.UTF8.GetBytes("second"));

            Assert.That(_storage.Read(0, "items"), Is.EqualTo(Encoding.UTF8.GetBytes("second")));
        }

        [Test]
        public void Write_LeavesAStaleTempFileHarmless()
        {
            _storage.Write(0, "items", Encoding.UTF8.GetBytes("the good save"));
            File.WriteAllText(_storage.GetSavePath(0, "items") + ".tmp", "{ half written");

            Assert.That(_storage.Read(0, "items"), Is.EqualTo(Encoding.UTF8.GetBytes("the good save")));
            Assert.That(_storage.ListSaveIds(0), Is.EqualTo(new[] { "items" }),
                "A leftover .tmp is not a save and must not be listed as one.");
        }

        [Test]
        public void Read_OnAMissingFile_ThrowsAStorageException()
        {
            Assert.Throws<SaveStorageException>(() => _storage.Read(0, "items"));
        }

        [Test]
        public void TryReadBackup_WithNoBackup_ReturnsFalse()
        {
            _storage.Write(0, "items", Encoding.UTF8.GetBytes("only write"));

            Assert.That(_storage.TryReadBackup(0, "items", out _), Is.False);
        }

        [Test]
        public void Quarantine_MovesTheFileAsideInsteadOfDeletingIt()
        {
            _storage.Write(0, "items", Encoding.UTF8.GetBytes("unreadable"));

            var quarantined = _storage.Quarantine(0, "items");

            Assert.That(quarantined, Is.Not.Null);
            Assert.That(File.Exists(quarantined), Is.True, "The player's only copy must survive.");
            Assert.That(_storage.Exists(0, "items"), Is.False);
        }

        [Test]
        public void Quarantine_WithNothingThere_ReturnsNull()
        {
            Assert.That(_storage.Quarantine(0, "items"), Is.Null);
        }

        [Test]
        public void Delete_RemovesTheSaveAndItsBackup()
        {
            _storage.Write(0, "items", Encoding.UTF8.GetBytes("first"));
            _storage.Write(0, "items", Encoding.UTF8.GetBytes("second"));

            _storage.Delete(0, "items");

            Assert.That(_storage.Exists(0, "items"), Is.False);
            Assert.That(_storage.TryReadBackup(0, "items", out _), Is.False);
        }

        [Test]
        public void ListSaveIds_ReturnsIdsNotFileNames()
        {
            _storage.Write(0, "items", Encoding.UTF8.GetBytes("a"));
            _storage.Write(0, "progress", Encoding.UTF8.GetBytes("b"));

            Assert.That(_storage.ListSaveIds(0), Is.EqualTo(new[] { "items", "progress" }));
        }

        [Test]
        public void ListSaveIds_OnAnUntouchedProfile_IsEmpty()
        {
            Assert.That(_storage.ListSaveIds(7), Is.Empty);
        }

        [Test]
        public void GetSavePath_RejectsAnIdThatWouldEscapeTheProfileFolder()
        {
            Assert.Throws<SaveException>(() => _storage.GetSavePath(0, "../../etc/passwd"));
        }
    }
}
