using System;

namespace ImplicitSave
{
    /// <summary>
    /// Declares the stable on-disk identity of a subtype stored polymorphically. The id becomes the
    /// <c>$t</c> discriminator written into the save file.
    /// </summary>
    /// <remarks>
    /// The .NET type name is never written to a save. Storing it would mean renaming a class, moving
    /// it to another assembly, or changing its namespace breaks every save that used it - and that
    /// is exactly the pain this package was built to remove.
    /// </remarks>
    /// <example>
    /// <code>
    /// [SaveType("melee_weapon")]
    /// public class MeleeWeapon : Weapon { }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class SaveTypeAttribute : Attribute
    {
        /// <summary>The value written as <c>$t</c>.</summary>
        public string Id { get; }

        /// <summary>
        /// Ids this type used to be written under. Saves holding any of them still load; new saves
        /// are written under <see cref="Id"/>.
        /// </summary>
        /// <remarks>
        /// Changing an id is normally a promise broken - every save already on disk names the old
        /// one. This is how to change it anyway, and the reason it is a separate list rather than a
        /// quiet fallback is that only you can know which old id meant THIS type. Nothing can infer
        /// that safely: guessing would put one subtype's data into another and report success.
        /// <para>
        /// Each save read under an old id is rewritten under the new one the next time the game
        /// saves, so the list only has to stay as long as players might still hold an old file.
        /// </para>
        /// </remarks>
        /// <example>
        /// <code>
        /// [SaveType("meteor", PreviousIds = new[] { "fireball" })]
        /// public class MeteorAbility : Ability { }
        /// </code>
        /// </example>
        public string[] PreviousIds { get; set; }

        /// <param name="id">
        /// Stable identity for this subtype. Prefer to choose it once and leave it alone; if you do
        /// have to change it, move the old value into <see cref="PreviousIds"/> so existing saves
        /// keep loading.
        /// </param>
        public SaveTypeAttribute(string id)
        {
            Id = id;
        }
    }
}
