using System;

namespace ImplicitSave
{
    /// <summary>
    /// Declares the stable on-disk identity of a <see cref="SaveData"/> class. The id becomes the
    /// save file name, so it survives renaming the class - which a player's save file must.
    /// </summary>
    /// <remarks>
    /// Without this attribute the id falls back to the type name in snake_case and a warning is
    /// logged, because a later rename would silently orphan every existing save.
    /// <para>
    /// Valid ids are lowercase letters, digits, underscore and hyphen.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// [SaveId("items")]
    /// public class ItemsSaveData : SaveData { }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class SaveIdAttribute : Attribute
    {
        /// <summary>The stable identity used as the save file name.</summary>
        public string Id { get; }

        /// <param name="id">
        /// Stable identity for this save type. Choose it once and never change it - changing it
        /// orphans every save file already on players' disks.
        /// </param>
        public SaveIdAttribute(string id)
        {
            Id = id;
        }
    }
}
