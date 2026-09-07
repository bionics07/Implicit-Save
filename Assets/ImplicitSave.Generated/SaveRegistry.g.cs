// ---------------------------------------------------------------------------
// SaveRegistry.g.cs - GENERATED FILE. Do not edit; your changes will be lost.
//
// Written by ImplicitSave, which lists every save type in your project here so
// that IL2CPP's managed stripping cannot delete them from a build. Types that
// nothing references are removed by the linker, and this package exists so you
// never have to write those references yourself - this file writes them for you.
//
// It regenerates when scripts recompile and again before every build. To force
// it: Tools > ImplicitSave > Regenerate Registry.
//
// Commit this file. Reading it is how you check what the package is doing.
// ---------------------------------------------------------------------------

namespace ImplicitSave.Generated
{
    internal static class SaveRegistry
    {
        [UnityEngine.RuntimeInitializeOnLoadMethod(
            UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]
        internal static void Register()
        {
            // save roots
            ImplicitSave.SaveTypeRegistry.Register("orphan", typeof(global::OrphanSaveData), () => new global::OrphanSaveData());
            ImplicitSave.SaveTypeRegistry.Register("sandbox", typeof(global::SandboxSaveData), () => new global::SandboxSaveData());

        }
    }
}
