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
}
