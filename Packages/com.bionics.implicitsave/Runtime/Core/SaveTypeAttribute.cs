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

        /// <param name="id">
        /// Stable identity for this subtype. Choose it once and never change it - changing it
        /// orphans every save that already holds this subtype.
        /// </param>
        public SaveTypeAttribute(string id)
        {
            Id = id;
        }
    }
}
