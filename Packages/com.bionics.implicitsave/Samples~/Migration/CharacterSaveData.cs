using System;
using Newtonsoft.Json.Linq;

namespace ImplicitSave.Samples.Migration
{
    /// <summary>
    /// A save class that changed twice after release. Players still have files from every version,
    /// so the old shapes have to keep loading.
    /// </summary>
    /// <remarks>
    /// History:
    /// <list type="bullet">
    /// <item><description>Version 1: <c>string Name</c>, <c>int Hp</c>.</description></item>
    /// <item><description>Version 2: <c>Hp</c> renamed to <c>Health</c>, and <c>MaxHealth</c> added.</description></item>
    /// <item><description>Version 3 (this class): <c>Name</c> split into <c>FirstName</c> and <c>LastName</c>.</description></item>
    /// </list>
    /// Raising <see cref="SchemaVersion"/> is what tells the package a file is old. Adding a field
    /// never needs a migration - a field missing from the file loads with its default. Renaming,
    /// moving or reshaping one does.
    /// </remarks>
    [SaveId(Id)]
    public class CharacterSaveData : SaveData
    {
        /// <summary>The file name. The prefix only keeps it apart from your own game's saves.</summary>
        public const string Id = "sample_migration_character";

        /// <inheritdoc />
        public override int SchemaVersion => 3;

        /// <summary>Given name.</summary>
        public string FirstName = "";

        /// <summary>Family name. Can be empty.</summary>
        public string LastName = "";

        /// <summary>Current health.</summary>
        public int Health = 100;

        /// <summary>Health cap.</summary>
        public int MaxHealth = 100;

        /// <inheritdoc />
        public override void ResetToDefaults()
        {
            FirstName = "";
            LastName = "";
            Health = 100;
            MaxHealth = 100;
        }
    }

    /// <summary>
    /// Version 1 to 2: <c>Hp</c> became <c>Health</c>. Declaring the class is enough for it to be
    /// found and applied.
    /// </summary>
    /// <remarks>
    /// A migration edits the raw JSON of the payload, because the old shape no longer has a C# class
    /// to load into. Keep every migration forever: a player who skipped two updates still has a
    /// version 1 file, and it goes through 1 to 2 and then 2 to 3.
    /// </remarks>
    public class CharacterMigration1To2 : ISaveMigration
    {
        /// <inheritdoc />
        public Type TargetType => typeof(CharacterSaveData);

        /// <inheritdoc />
        public int FromVersion => 1;

        /// <inheritdoc />
        public int ToVersion => 2;

        /// <inheritdoc />
        public JObject Migrate(JObject data)
        {
            var hp = data["Hp"];
            if (hp != null)
            {
                data["Health"] = hp;
                data.Remove("Hp");
            }

            // MaxHealth did not exist yet. Leaving it out would load the default of 100, but a hero
            // who already had more health than that would then be over the cap.
            data["MaxHealth"] = Math.Max(100, (int?)hp ?? 0);

            return data;
        }
    }

    /// <summary>Version 2 to 3: <c>Name</c> split into <c>FirstName</c> and <c>LastName</c>.</summary>
    public class CharacterMigration2To3 : ISaveMigration
    {
        /// <inheritdoc />
        public Type TargetType => typeof(CharacterSaveData);

        /// <inheritdoc />
        public int FromVersion => 2;

        /// <inheritdoc />
        public int ToVersion => 3;

        /// <inheritdoc />
        public JObject Migrate(JObject data)
        {
            var name = ((string)data["Name"] ?? "").Trim();
            var space = name.IndexOf(' ');

            data["FirstName"] = space < 0 ? name : name.Substring(0, space);
            data["LastName"] = space < 0 ? "" : name.Substring(space + 1);
            data.Remove("Name");

            return data;
        }
    }
}
