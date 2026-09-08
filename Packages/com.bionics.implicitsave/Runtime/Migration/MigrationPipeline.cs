using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace ImplicitSave
{
    /// <summary>
    /// Walks a save payload from the version it was written at up to the version the current class
    /// expects, one step at a time.
    /// </summary>
    /// <remarks>
    /// Steps are applied in order and each one only has to know about its own change. A player
    /// coming back after two updates goes 1→2→3 without anyone writing a 1→3 migration.
    /// <para>
    /// A missing step is an error, never a silent skip. Skipping would hand the deserializer a shape
    /// it does not understand, and the result is a save that loads with default values - the player
    /// sees their progress gone and there is nothing in the log to explain it.
    /// </para>
    /// </remarks>
    public sealed class MigrationPipeline
    {
        private readonly Dictionary<Type, Dictionary<int, ISaveMigration>> _byType =
            new Dictionary<Type, Dictionary<int, ISaveMigration>>();

        /// <summary>Creates an empty pipeline.</summary>
        public MigrationPipeline()
        {
        }

        /// <summary>Creates a pipeline holding the given migrations.</summary>
        public MigrationPipeline(IEnumerable<ISaveMigration> migrations)
        {
            if (migrations == null)
            {
                return;
            }

            foreach (var migration in migrations)
            {
                Add(migration);
            }
        }

        /// <summary>How many migrations are registered.</summary>
        public int Count { get; private set; }

        /// <summary>Adds a migration. A second one for the same step is refused and reported.</summary>
        public void Add(ISaveMigration migration)
        {
            if (migration?.TargetType == null)
            {
                return;
            }

            if (migration.ToVersion <= migration.FromVersion)
            {
                ImplicitSaveLog.Error(
                    $"'{migration.GetType().Name}' migrates from {migration.FromVersion} to " +
                    $"{migration.ToVersion}, which does not move forward. It was ignored.");
                return;
            }

            if (!_byType.TryGetValue(migration.TargetType, out var steps))
            {
                steps = new Dictionary<int, ISaveMigration>();
                _byType[migration.TargetType] = steps;
            }

            if (steps.TryGetValue(migration.FromVersion, out var existing))
            {
                if (existing.GetType() == migration.GetType())
                {
                    return;
                }

                // Two ways to migrate the same step means the result depends on which one ran, and
                // nobody can tell which by reading a save file afterwards.
                ImplicitSaveLog.Error(
                    $"'{existing.GetType().Name}' and '{migration.GetType().Name}' both migrate " +
                    $"{migration.TargetType.Name} from version {migration.FromVersion}. Keeping the first.");
                return;
            }

            steps[migration.FromVersion] = migration;
            Count++;
        }

        /// <summary>Whether any migration is registered for this save type.</summary>
        public bool HasMigrations(Type targetType)
        {
            return _byType.ContainsKey(targetType);
        }

        /// <summary>
        /// Brings a payload from <paramref name="fromVersion"/> up to
        /// <paramref name="toVersion"/>.
        /// </summary>
        /// <param name="targetType">The save class the payload belongs to.</param>
        /// <param name="data">The payload as written.</param>
        /// <param name="fromVersion">The version in the file.</param>
        /// <param name="toVersion">The version the class expects.</param>
        /// <exception cref="MigrationMissingException">A step in the chain does not exist.</exception>
        /// <exception cref="SaveException">A migration threw, or returned nothing.</exception>
        public JObject Migrate(Type targetType, JObject data, int fromVersion, int toVersion)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (fromVersion >= toVersion)
            {
                return data;
            }

            _byType.TryGetValue(targetType, out var steps);

            var current = data;
            var version = fromVersion;

            while (version < toVersion)
            {
                if (steps == null || !steps.TryGetValue(version, out var migration))
                {
                    throw new MigrationMissingException(targetType, version, toVersion);
                }

                current = Apply(migration, current, targetType);

                // Trust the migration's own ToVersion rather than assuming +1, so a single step can
                // cover a jump if its author chose to write it that way.
                version = migration.ToVersion;
            }

            ImplicitSaveLog.Info(
                $"Migrated '{targetType.Name}' from schema version {fromVersion} to {version}.");

            return current;
        }

        private static JObject Apply(ISaveMigration migration, JObject data, Type targetType)
        {
            JObject result;

            try
            {
                result = migration.Migrate(data);
            }
            catch (Exception e)
            {
                throw new SaveException(
                    $"'{migration.GetType().Name}' failed while migrating {targetType.Name} from version " +
                    $"{migration.FromVersion} to {migration.ToVersion}.", e);
            }

            if (result == null)
            {
                throw new SaveException(
                    $"'{migration.GetType().Name}' returned null. A migration has to return the payload, " +
                    "either the object it was given or a new one.");
            }

            return result;
        }
    }
}
