using System;

namespace ImplicitSave
{
    /// <summary>
    /// Base type for every exception ImplicitSave surfaces. Raw IO exceptions never cross the
    /// public API - they are wrapped in this or one of its subclasses.
    /// </summary>
    public class SaveException : Exception
    {
        /// <param name="message">Human-readable description of the failure.</param>
        public SaveException(string message) : base(message)
        {
        }

        /// <param name="message">Human-readable description of the failure.</param>
        /// <param name="inner">The underlying failure, kept for diagnostics.</param>
        public SaveException(string message, Exception inner) : base(message, inner)
        {
        }
    }

    /// <summary>
    /// A save file could not be read as valid data, and neither could its backup. The unreadable
    /// file is quarantined rather than deleted - it may be the player's only copy.
    /// </summary>
    public class SaveCorruptedException : SaveException
    {
        /// <summary>Identity of the save that failed to load.</summary>
        public string SaveId { get; }

        /// <summary>Profile the failing file belongs to.</summary>
        public int ProfileId { get; }

        /// <summary>Where the unreadable file was moved to, or <c>null</c> if it could not be moved.</summary>
        public string QuarantinedPath { get; }

        /// <param name="saveId">Identity of the save that failed to load.</param>
        /// <param name="profileId">Profile the failing file belongs to.</param>
        /// <param name="quarantinedPath">Where the unreadable file was moved to.</param>
        /// <param name="inner">The underlying parse or IO failure.</param>
        public SaveCorruptedException(string saveId, int profileId, string quarantinedPath, Exception inner)
            : base($"Save '{saveId}' of profile {profileId} could not be read, and its backup did not " +
                   $"recover it. The file was kept at '{quarantinedPath}'. A fresh instance was returned.", inner)
        {
            SaveId = saveId;
            ProfileId = profileId;
            QuarantinedPath = quarantinedPath;
        }
    }

    /// <summary>
    /// Storage could not be read or written - permissions, a missing directory, a full disk.
    /// </summary>
    public class SaveStorageException : SaveException
    {
        /// <param name="message">Human-readable description of the failure.</param>
        /// <param name="inner">The underlying IO failure.</param>
        public SaveStorageException(string message, Exception inner) : base(message, inner)
        {
        }
    }

    /// <summary>
    /// A save could not be converted to or from its stored form. Usually a type that left Unity's
    /// serializable subset.
    /// </summary>
    public class SaveSerializationException : SaveException
    {
        /// <param name="message">Human-readable description of the failure.</param>
        /// <param name="inner">The underlying serializer failure.</param>
        public SaveSerializationException(string message, Exception inner) : base(message, inner)
        {
        }
    }

    /// <summary>
    /// The file was written by a newer version of the game than the one running. It is left
    /// untouched and the save is loaded read-only.
    /// </summary>
    /// <remarks>
    /// Usually a player who rolled back a build, or who shares a save between two machines. The one
    /// thing that must not happen here is overwriting: the newer file holds progress this build
    /// cannot even represent, and writing over it destroys it for good.
    /// </remarks>
    public class FutureVersionException : SaveException
    {
        /// <summary>Identity of the save.</summary>
        public string SaveId { get; }

        /// <summary>Schema version found in the file.</summary>
        public int FileVersion { get; }

        /// <summary>Schema version this build understands.</summary>
        public int SupportedVersion { get; }

        /// <param name="saveId">Identity of the save.</param>
        /// <param name="fileVersion">Schema version found in the file.</param>
        /// <param name="supportedVersion">Schema version this build understands.</param>
        public FutureVersionException(string saveId, int fileVersion, int supportedVersion)
            : base($"Save '{saveId}' is at schema version {fileVersion}, but this build only understands " +
                   $"{supportedVersion}. It was written by a newer version of the game. The file was left " +
                   "untouched and the save is read-only, so nothing is overwritten.")
        {
            SaveId = saveId;
            FileVersion = fileVersion;
            SupportedVersion = supportedVersion;
        }
    }

    /// <summary>
    /// An older save cannot be brought up to date because a step in the migration chain is missing.
    /// </summary>
    public class MigrationMissingException : SaveException
    {
        /// <summary>The save class being migrated.</summary>
        public Type TargetType { get; }

        /// <summary>The version the chain got stuck at.</summary>
        public int FromVersion { get; }

        /// <param name="targetType">The save class being migrated.</param>
        /// <param name="fromVersion">The version the chain got stuck at.</param>
        /// <param name="toVersion">The version it needed to reach.</param>
        public MigrationMissingException(Type targetType, int fromVersion, int toVersion)
            : base($"'{targetType.Name}' has a save at schema version {fromVersion} and needs to reach " +
                   $"{toVersion}, but no migration from {fromVersion} to {fromVersion + 1} exists. Write an " +
                   $"ISaveMigration with FromVersion {fromVersion} and ToVersion {fromVersion + 1}.")
        {
            TargetType = targetType;
            FromVersion = fromVersion;
        }
    }
}
