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
            ImplicitSave.SaveTypeRegistry.Register("demo", typeof(global::DemoChangeNameSaveData), () => new global::DemoChangeNameSaveData());
            ImplicitSave.SaveTypeRegistry.Register("orphan", typeof(global::OrphanSaveData), () => new global::OrphanSaveData());
            ImplicitSave.SaveTypeRegistry.Register("sandbox", typeof(global::SandboxSaveData), () => new global::SandboxSaveData());

            // polymorphic subtypes
            ImplicitSave.SaveTypeRegistry.RegisterSubtype("ability_combo", typeof(global::ComboAbility), () => new global::ComboAbility());
            ImplicitSave.SaveTypeRegistry.RegisterSubtype("ability_heal", typeof(global::HealAbility), () => new global::HealAbility());
            ImplicitSave.SaveTypeRegistry.RegisterSubtype("ability_meteor", typeof(global::MetorAbility), () => new global::MetorAbility());
            ImplicitSave.SaveTypeRegistry.RegisterSubtype("orphan_gold_badge", typeof(global::OrphanGoldBadge), () => new global::OrphanGoldBadge());

            // migrations
            ImplicitSave.SaveTypeRegistry.RegisterMigration(new global::DemoMigration1To2());

        }
    }
}
