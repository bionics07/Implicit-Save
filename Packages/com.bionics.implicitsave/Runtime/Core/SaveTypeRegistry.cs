using System;
using System.Collections.Generic;
using UnityEngine;

namespace ImplicitSave
{
    /// <summary>Where a registry entry came from, so the user can see there is no magic.</summary>
    public enum SaveTypeOrigin
    {
        /// <summary>Found by the editor scanning the project. Editor only.</summary>
        EditorScan,

        /// <summary>Read from the generated <c>SaveRegistry.g.cs</c>. This is what ships.</summary>
        GeneratedRegistry
    }

    /// <summary>One save type the package knows about.</summary>
    public readonly struct SaveTypeEntry
    {
        /// <summary>The stable id: a file name for a save root, a <c>$t</c> value for a subtype.</summary>
        public readonly string Id;

        /// <summary>The type itself.</summary>
        public readonly Type Type;

        /// <summary>Whether this is a save root or a polymorphic subtype.</summary>
        public readonly bool IsSubtype;

        /// <summary>Where the entry came from.</summary>
        public readonly SaveTypeOrigin Origin;

        internal readonly Func<object> Factory;

        internal SaveTypeEntry(string id, Type type, bool isSubtype, SaveTypeOrigin origin, Func<object> factory)
        {
            Id = id;
            Type = type;
            IsSubtype = isSubtype;
            Origin = origin;
            Factory = factory;
        }
    }

    /// <summary>
    /// The list of save types the package knows about, and the map between a stable id and a .NET
    /// type in both directions.
    /// </summary>
    /// <remarks>
    /// This exists because of how a build differs from the editor.
    /// <para>
    /// In the editor, types are found by scanning the project, which is instant and always current.
    /// That scan lives in <c>UnityEditor</c> and does not exist in a build.
    /// </para>
    /// <para>
    /// In a build, IL2CPP's managed stripping deletes types nothing references - and the whole point
    /// of this package is that you never write a reference to your save class. A generated
    /// <c>SaveRegistry.g.cs</c> names every type explicitly, which both keeps the linker from
    /// removing them and removes any need to scan at startup.
    /// </para>
    /// <para>
    /// The file is deliberately plain, readable C#. "Implicit" should not mean "you cannot see what
    /// happened".
    /// </para>
    /// </remarks>
    public static class SaveTypeRegistry
    {
        private static readonly Dictionary<string, SaveTypeEntry> ById =
            new Dictionary<string, SaveTypeEntry>(StringComparer.Ordinal);

        private static readonly Dictionary<Type, string> IdByType = new Dictionary<Type, string>();

        /// <summary>
        /// Ids a type used to be written under. Read-only: a lookup falls back here, but nothing is
        /// ever WRITTEN under an old id, so every save that passes through the game migrates itself
        /// forward.
        /// </summary>
        private static readonly Dictionary<string, Type> TypeByPreviousId =
            new Dictionary<string, Type>(StringComparer.Ordinal);
        private static readonly List<SaveTypeEntry> Entries = new List<SaveTypeEntry>();
        private static readonly List<ISaveMigration> Migrations = new List<ISaveMigration>();
        private static readonly HashSet<Type> MigrationTypes = new HashSet<Type>();

        /// <summary>
        /// Installed by the editor so the registry can fill itself by scanning the project. Null in
        /// a build, where the generated file does the registering instead.
        /// </summary>
        /// <remarks>
        /// Not cleared by the play-mode reset: it is wiring installed once per domain load, not
        /// state belonging to a play session. Clearing it would leave the editor unable to find any
        /// save type after the first play.
        /// </remarks>
        internal static Action EditorDiscovery;

        private static bool _discoveryRan;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Runs before the generated Register() at BeforeSceneLoad, so a play session always
            // starts from an empty registry even with Disable Domain Reload on.
            ById.Clear();
            IdByType.Clear();
            Entries.Clear();
            Migrations.Clear();
            MigrationTypes.Clear();
            TypeByPreviousId.Clear();
            _discoveryRan = false;
        }

        /// <summary>Every known type, save roots and subtypes, ordered by id.</summary>
        public static IReadOnlyList<SaveTypeEntry> GetAll()
        {
            EnsureDiscovered();
            return Entries;
        }

        /// <summary>How many types are registered.</summary>
        public static int Count
        {
            get
            {
                EnsureDiscovered();
                return Entries.Count;
            }
        }

        /// <summary>Registers a save root. Called by the generated registry and by the editor scan.</summary>
        /// <param name="saveId">The stable id, which becomes the file name.</param>
        /// <param name="type">The save type.</param>
        /// <param name="factory">Creates an instance. Written out explicitly so no reflection is needed.</param>
        public static void Register(string saveId, Type type, Func<SaveData> factory)
        {
            Add(new SaveTypeEntry(saveId, type, isSubtype: false, SaveTypeOrigin.GeneratedRegistry, () => factory()));
        }

        /// <summary>Registers a polymorphic subtype, identified by its <c>$t</c> value.</summary>
        public static void RegisterSubtype(string typeId, Type type, Func<object> factory)
        {
            Add(new SaveTypeEntry(typeId, type, isSubtype: true, SaveTypeOrigin.GeneratedRegistry, factory));
        }

        /// <summary>
        /// Registers an id this type used to be written under, so old saves still resolve.
        /// </summary>
        /// <remarks>
        /// Deliberately one-way. The type still reports its current id, so anything read under
        /// <paramref name="previousId"/> is written back under the new one - the file heals itself
        /// the first time the game saves, and the alias eventually stops being reachable.
        /// </remarks>
        public static void RegisterPreviousId(string previousId, Type type)
        {
            if (string.IsNullOrEmpty(previousId) || type == null)
            {
                return;
            }

            if (ById.TryGetValue(previousId, out var live))
            {
                // Another type is answering to this id right now. Honouring the alias would hand one
                // type's saves to another, which is worse than the rename it was meant to fix.
                ImplicitSaveLog.Error(
                    $"'{type.FullName}' lists '{previousId}' as a previous id, but '{live.Type.FullName}' uses " +
                    "that id today. Ignoring it - the live type wins.");
                return;
            }

            if (TypeByPreviousId.TryGetValue(previousId, out var other) && other != type)
            {
                ImplicitSaveLog.Error(
                    $"Both '{other.FullName}' and '{type.FullName}' claim '{previousId}' as a previous id. " +
                    "Only one of them can have been it; keeping the first.");
                return;
            }

            TypeByPreviousId[previousId] = type;
        }

        /// <summary>
        /// Registers a migration. Called by the generated registry and by the editor scan; a game
        /// never calls it.
        /// </summary>
        public static void RegisterMigration(ISaveMigration migration)
        {
            if (migration == null || !MigrationTypes.Add(migration.GetType()))
            {
                return;
            }

            Migrations.Add(migration);
        }

        /// <summary>Every migration found in the project.</summary>
        public static IReadOnlyList<ISaveMigration> GetMigrations()
        {
            EnsureDiscovered();
            return Migrations;
        }

        internal static void Add(SaveTypeEntry entry)
        {
            if (string.IsNullOrEmpty(entry.Id) || entry.Type == null)
            {
                return;
            }

            if (ById.TryGetValue(entry.Id, out var existing))
            {
                if (existing.Type == entry.Type)
                {
                    return;
                }

                // Two types claiming one id means one of them silently loads the other's file. The
                // build-time validation refuses this; reaching here means something went around it.
                ImplicitSaveLog.Error(
                    $"'{entry.Id}' is claimed by both '{existing.Type.FullName}' and '{entry.Type.FullName}'. " +
                    "Give one of them a different id - as it stands they would overwrite each other's save.");
                return;
            }

            ById[entry.Id] = entry;
            IdByType[entry.Type] = entry.Id;
            Entries.Add(entry);
            Entries.Sort((a, b) => string.CompareOrdinal(a.Id, b.Id));
        }

        /// <summary>Finds the type registered under an id.</summary>
        public static bool TryGetType(string id, out Type type)
        {
            EnsureDiscovered();

            if (ById.TryGetValue(id, out var entry))
            {
                type = entry.Type;
                return true;
            }

            // Only after the live ids: a current id must never be shadowed by someone's old one.
            return TypeByPreviousId.TryGetValue(id, out type);
        }

        /// <summary>Whether this id is one a type used to be written under, rather than its id today.</summary>
        public static bool IsPreviousId(string id)
        {
            EnsureDiscovered();
            return !ById.ContainsKey(id) && TypeByPreviousId.ContainsKey(id);
        }

        /// <summary>Finds the id a type is registered under.</summary>
        public static bool TryGetId(Type type, out string id)
        {
            EnsureDiscovered();
            return IdByType.TryGetValue(type, out id);
        }

        /// <summary>Creates an instance of the type registered under an id.</summary>
        /// <returns>The new instance, or <c>null</c> if the id is unknown.</returns>
        public static object Create(string id)
        {
            EnsureDiscovered();

            if (ById.TryGetValue(id, out var entry))
            {
                return entry.Factory?.Invoke();
            }

            // An old id still creates the type it became, so a save written before a rename can be
            // read back without the caller knowing a rename happened.
            return TypeByPreviousId.TryGetValue(id, out var renamed) && IdByType.TryGetValue(renamed, out var current)
                   && ById.TryGetValue(current, out var target)
                ? target.Factory?.Invoke()
                : null;
        }

        /// <summary>Every registered save root, for wiping a profile or listing what a game stores.</summary>
        public static IReadOnlyList<Type> GetSaveTypes()
        {
            EnsureDiscovered();
            var types = new List<Type>();

            foreach (var entry in Entries)
            {
                if (!entry.IsSubtype)
                {
                    types.Add(entry.Type);
                }
            }

            return types;
        }

        private static void EnsureDiscovered()
        {
            if (_discoveryRan)
            {
                return;
            }

            _discoveryRan = true;
            EditorDiscovery?.Invoke();
        }

        /// <summary>
        /// Shouts if a build shipped without its generated registry, instead of failing quietly the
        /// first time a player saves.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void WarnIfEmpty()
        {
            if (Application.isEditor || Entries.Count > 0)
            {
                return;
            }

            ImplicitSaveLog.Error(
                "No save types are registered. The generated registry was probably not included in this " +
                "build, which means saving will not work. Run Tools > ImplicitSave > Regenerate Registry " +
                "and build again.");
        }
    }
}
