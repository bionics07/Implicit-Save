using UnityEngine;

namespace ImplicitSave.Editor
{
    /// <summary>
    /// A throwaway <c>ScriptableObject</c> that holds one save instance so Unity's own inspector can
    /// draw it.
    /// </summary>
    /// <remarks>
    /// Unity draws through <c>SerializedObject</c>, and a <c>SerializedObject</c> needs a
    /// <c>UnityEngine.Object</c> to wrap. Save data is a plain C# class, so this is the wrapper.
    /// <para>
    /// Going through Unity's inspector rather than drawing the JSON by hand is what gets property
    /// drawers, undo, arrays and nesting for free - all of which would otherwise have to be built
    /// and maintained here.
    /// </para>
    /// <para>
    /// The field is <c>[SerializeReference]</c> because Unity does not do polymorphism on an
    /// ordinary field: declared as <see cref="SaveData"/>, a plain field would show only the base
    /// class's members and none of the actual save's.
    /// </para>
    /// <para>
    /// Created when the window opens, destroyed when it closes, and never written as an asset.
    /// </para>
    /// </remarks>
    internal class SaveProxy : ScriptableObject
    {
        /// <summary>The save being edited.</summary>
        [SerializeReference] public SaveData Data;

        /// <summary>Creates a throwaway proxy around a save instance.</summary>
        /// <remarks>
        /// <c>DontSave</c> and not <c>HideAndDontSave</c>: the latter also carries
        /// <see cref="HideFlags.NotEditable"/>, and a <c>SerializedObject</c> over a not-editable
        /// target draws every field greyed out. The window looked correct and nothing could be
        /// typed into it.
        /// </remarks>
        internal static SaveProxy Create(SaveData data)
        {
            var proxy = CreateInstance<SaveProxy>();
            proxy.hideFlags = HideFlags.DontSave;
            proxy.Data = data;
            return proxy;
        }
    }
}
