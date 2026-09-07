using System.Collections.Generic;

namespace ImplicitSave.Storage
{
    /// <summary>
    /// Where save bytes live. Abstracting this is what lets the whole system be tested without
    /// touching a disk, and what will let a later version target WebGL or the cloud.
    /// </summary>
    /// <remarks>
    /// Implementations deal in bytes and never in save types. Writes must be atomic: a process
    /// killed mid-write leaves either the previous content or the new one, never a half file.
    /// </remarks>
    public interface ISaveStorage
    {
        /// <summary>Whether a save with this id exists in this profile.</summary>
        bool Exists(int profileId, string saveId);

        /// <summary>Reads the stored bytes.</summary>
        /// <exception cref="SaveStorageException">The entry is missing or unreadable.</exception>
        byte[] Read(int profileId, string saveId);

        /// <summary>
        /// Reads the backup copy left by the previous write, if there is one. This is the first
        /// recovery step when the main file will not parse.
        /// </summary>
        /// <returns><c>true</c> when <paramref name="content"/> was filled.</returns>
        bool TryReadBackup(int profileId, string saveId, out byte[] content);

        /// <summary>
        /// Writes atomically, keeping the previous content as the backup.
        /// </summary>
        /// <exception cref="SaveStorageException">The write could not be completed.</exception>
        void Write(int profileId, string saveId, byte[] content);

        /// <summary>
        /// Moves an unreadable entry aside instead of deleting it, and returns where it went.
        /// It may be the player's only copy, so it is never destroyed.
        /// </summary>
        /// <returns>The new location, or <c>null</c> if nothing was there to move.</returns>
        string Quarantine(int profileId, string saveId);

        /// <summary>Deletes the entry and its backup.</summary>
        void Delete(int profileId, string saveId);

        /// <summary>Lists the save ids stored for a profile.</summary>
        IReadOnlyList<string> ListSaveIds(int profileId);

        // ---- Profiles ----
        //
        // The profile index sits beside the profile folders rather than inside one, so it needs its
        // own three methods. Where things physically live is storage's business; what a profile
        // means is ProfileService's.

        /// <summary>Whether a profile index has been written yet.</summary>
        bool IndexExists();

        /// <summary>Reads the profile index.</summary>
        /// <exception cref="SaveStorageException">The index is missing or unreadable.</exception>
        byte[] ReadIndex();

        /// <summary>Writes the profile index, atomically and with a backup, like any save.</summary>
        /// <exception cref="SaveStorageException">The write could not be completed.</exception>
        void WriteIndex(byte[] content);

        /// <summary>Moves an unreadable index aside and returns where it went.</summary>
        /// <returns>The new location, or <c>null</c> if there was nothing to move.</returns>
        string QuarantineIndex();

        /// <summary>
        /// Lists the profiles that physically exist, whatever the index claims. This is what makes
        /// a lost index recoverable instead of fatal.
        /// </summary>
        IReadOnlyList<int> ListProfileIds();

        /// <summary>Whether a profile folder exists.</summary>
        bool ProfileExists(int profileId);

        /// <summary>Deletes a profile and everything in it.</summary>
        void DeleteProfile(int profileId);

        /// <summary>
        /// Copies every save of one profile over another, replacing what was there.
        /// </summary>
        void CopyProfile(int sourceProfileId, int destinationProfileId);
    }
}
