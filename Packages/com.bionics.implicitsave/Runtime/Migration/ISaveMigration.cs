using System;
using Newtonsoft.Json.Linq;

namespace ImplicitSave
{
    /// <summary>
    /// Rewrites an older save file into the shape the current class expects. Declare one and it is
    /// found and applied on its own - there is nothing to register.
    /// </summary>
    /// <remarks>
    /// This is what makes a game patchable. The first update that renames or reshapes a field turns
    /// every save already on players' disks into a file the new code cannot read: the field it wants
    /// is not there, so it silently gets a default, and the player's progress is gone with no error
    /// to explain it.
    /// <para>
    /// A migration works on the raw JSON tree, before any type is involved. That is deliberate - the
    /// old shape no longer has a C# class to deserialize into.
    /// </para>
    /// <para>
    /// One migration covers one step. Going from version 1 to 3 is two migrations, 1→2 and 2→3,
    /// applied in order. Keep old ones around forever: a player who has not opened the game in two
    /// years still starts at version 1.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // v1: public int Coins;
    /// // v2: public CurrencyData Currency;   (Coins became Currency.Gold)
    /// public class ItemsMigration1To2 : ISaveMigration
    /// {
    ///     public Type TargetType =&gt; typeof(ItemsSaveData);
    ///     public int FromVersion =&gt; 1;
    ///     public int ToVersion =&gt; 2;
    ///
    ///     public JObject Migrate(JObject data)
    ///     {
    ///         var coins = data["Coins"]?.Value&lt;int&gt;() ?? 0;
    ///         data.Remove("Coins");
    ///         data["Currency"] = new JObject { ["Gold"] = coins, ["Gems"] = 0 };
    ///         return data;
    ///     }
    /// }
    /// </code>
    /// </example>
    public interface ISaveMigration
    {
        /// <summary>The save class this migration applies to.</summary>
        Type TargetType { get; }

        /// <summary>The schema version this migration reads.</summary>
        int FromVersion { get; }

        /// <summary>
        /// The schema version this migration produces. Normally
        /// <see cref="FromVersion"/> + 1.
        /// </summary>
        int ToVersion { get; }

        /// <summary>
        /// Rewrites the payload. Return the same object after mutating it, or a new one.
        /// </summary>
        /// <param name="data">The save payload in its <see cref="FromVersion"/> shape.</param>
        JObject Migrate(JObject data);
    }
}
