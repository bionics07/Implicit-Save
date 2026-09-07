using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace ImplicitSave.Storage
{
    /// <summary>
    /// Stores saves as files under <c>{persistentDataPath}/{folder}/{profileId}/{saveId}.json</c>.
    /// </summary>
    /// <remarks>
    /// This is the only type in the package that knows about <c>Application.persistentDataPath</c>.
    /// Everything above it works against <see cref="ISaveStorage"/>, which is what keeps the rest of
    /// the system testable without a disk.
    /// </remarks>
    public sealed class FileSaveStorage : ISaveStorage
    {
        private const string SaveExtension = ".json";
        private const string TempExtension = ".tmp";
        private const string BackupExtension = ".bak";
        private const string CorruptExtension = ".corrupt";

        private readonly string _rootPath;

        /// <summary>Absolute path of the folder holding every profile.</summary>
        public string RootPath => _rootPath;

        /// <summary>
        /// Test seam: invoked with the temp file path after it is written and before it replaces the
        /// real file. Throwing from it simulates a process dying mid-write.
        /// </summary>
        internal Action<string> AfterTempWritten;

        /// <param name="folderName">Folder under the persistent data path. Defaults to <c>saves</c>.</param>
        public FileSaveStorage(string folderName = "saves")
            : this(Application.persistentDataPath, folderName)
        {
        }

        /// <param name="basePath">Directory the save folder is created in.</param>
        /// <param name="folderName">Folder under <paramref name="basePath"/>.</param>
        public FileSaveStorage(string basePath, string folderName)
        {
            if (string.IsNullOrEmpty(basePath))
            {
                throw new ArgumentException("Base path must not be empty.", nameof(basePath));
            }

            _rootPath = string.IsNullOrEmpty(folderName) ? basePath : Path.Combine(basePath, folderName);
        }

        /// <inheritdoc />
        public bool Exists(int profileId, string saveId)
        {
            return File.Exists(GetSavePath(profileId, saveId));
        }

        /// <inheritdoc />
        public byte[] Read(int profileId, string saveId)
        {
            var path = GetSavePath(profileId, saveId);

            try
            {
                return File.ReadAllBytes(path);
            }
            catch (Exception e)
            {
                throw new SaveStorageException($"Could not read '{path}'.", e);
            }
        }

        /// <inheritdoc />
        public bool TryReadBackup(int profileId, string saveId, out byte[] content)
        {
            var path = GetSavePath(profileId, saveId) + BackupExtension;
            content = null;

            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                content = File.ReadAllBytes(path);
                return true;
            }
            catch (Exception e)
            {
                ImplicitSaveLog.Warning($"Backup '{path}' exists but could not be read: {e.Message}");
                return false;
            }
        }

        /// <inheritdoc />
        public void Write(int profileId, string saveId, byte[] content)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            var path = GetSavePath(profileId, saveId);
            var tempPath = path + TempExtension;
            var backupPath = path + BackupExtension;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? _rootPath);

                // Write the whole payload somewhere harmless first. Until the swap below happens,
                // the file a player would load is still the previous, intact one.
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    stream.Write(content, 0, content.Length);
                    stream.Flush(flushToDisk: true);
                }

                AfterTempWritten?.Invoke(tempPath);

                Swap(tempPath, path, backupPath);
            }
            catch (Exception e)
            {
                DeleteQuietly(tempPath);
                throw new SaveStorageException($"Could not write '{path}'.", e);
            }
        }

        /// <inheritdoc />
        public string Quarantine(int profileId, string saveId)
        {
            var path = GetSavePath(profileId, saveId);
            if (!File.Exists(path))
            {
                return null;
            }

            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var target = $"{path}{CorruptExtension}.{stamp}";

            try
            {
                if (File.Exists(target))
                {
                    File.Delete(target);
                }

                File.Move(path, target);
                return target;
            }
            catch (Exception e)
            {
                // Quarantine failing must not mask the corruption it was reacting to.
                ImplicitSaveLog.Warning($"Could not move the unreadable '{path}' aside: {e.Message}");
                return null;
            }
        }

        /// <inheritdoc />
        public void Delete(int profileId, string saveId)
        {
            var path = GetSavePath(profileId, saveId);
            DeleteQuietly(path);
            DeleteQuietly(path + BackupExtension);
            DeleteQuietly(path + TempExtension);
        }

        /// <inheritdoc />
        public IReadOnlyList<string> ListSaveIds(int profileId)
        {
            var directory = GetProfilePath(profileId);
            if (!Directory.Exists(directory))
            {
                return Array.Empty<string>();
            }

            var files = Directory.GetFiles(directory, "*" + SaveExtension);
            var ids = new List<string>(files.Length);

            foreach (var file in files)
            {
                ids.Add(Path.GetFileNameWithoutExtension(file));
            }

            ids.Sort(StringComparer.Ordinal);
            return ids;
        }

        /// <summary>Absolute path of a profile's folder.</summary>
        public string GetProfilePath(int profileId)
        {
            return Path.Combine(_rootPath, profileId.ToString(CultureInfo.InvariantCulture));
        }

        /// <summary>Absolute path of one save file.</summary>
        public string GetSavePath(int profileId, string saveId)
        {
            if (!SaveIdResolver.IsValidId(saveId, out var reason))
            {
                throw new SaveException($"'{saveId}' is not a usable save id: {reason}.");
            }

            return Path.Combine(GetProfilePath(profileId), saveId + SaveExtension);
        }

        /// <summary>
        /// Puts the temp file in place of the real one, keeping the previous content as backup.
        /// </summary>
        private static void Swap(string tempPath, string path, string backupPath)
        {
            if (!File.Exists(path))
            {
                File.Move(tempPath, path);
                return;
            }

            try
            {
                File.Replace(tempPath, path, backupPath);
            }
            catch (Exception e) when (e is PlatformNotSupportedException || e is IOException || e is UnauthorizedAccessException)
            {
                // File.Replace is not supported on every filesystem Unity ships to - some Android
                // ones and WebGL among them. This path is wider than a single rename, but it still
                // never leaves the destination holding a partial payload.
                ImplicitSaveLog.Info($"File.Replace unavailable here ({e.GetType().Name}); using copy and move.");

                File.Copy(path, backupPath, overwrite: true);
                File.Delete(path);
                File.Move(tempPath, path);
            }
        }

        private static void DeleteQuietly(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception e)
            {
                ImplicitSaveLog.Info($"Could not delete '{path}': {e.Message}");
            }
        }
    }
}
