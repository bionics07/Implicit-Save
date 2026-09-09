using System;
using System.Collections.Generic;
using UnityEditor;

namespace ImplicitSave.Editor
{
    /// <summary>
    /// Rewrites the generated registry just before a script holding a save type is deleted.
    /// </summary>
    /// <remarks>
    /// Deleting a save class used to wedge the project, and the order of events is the whole reason.
    /// <c>SaveRegistry.g.cs</c> names every save type in plain C# - which is what stops IL2CPP from
    /// stripping them - so the moment the class is gone that file no longer compiles. And the
    /// generator that would fix it runs after a successful compile, which now never happens. The
    /// project sits there with an error whose only manual fix is deleting a file the package told
    /// you not to edit.
    /// <para>
    /// <see cref="AssetModificationProcessor"/> is the way out: <c>OnWillDeleteAsset</c> runs while
    /// the old assemblies are still loaded and the deletion has not happened yet. Regenerating here,
    /// with the doomed type excluded, means the compile that follows sees a registry that never
    /// mentioned it.
    /// </para>
    /// <para>
    /// This only covers deleting a whole script, and only the type Unity associates with the file
    /// name - removing one class from a file that keeps existing never reaches here, because that is
    /// an edit, not a deletion. <see cref="SaveRegistryRecovery"/> is what catches the rest, after
    /// the fact.
    /// </para>
    /// </remarks>
    internal sealed class SaveTypeDeletionWatcher : AssetModificationProcessor
    {
        private static readonly HashSet<Type> Doomed = new HashSet<Type>();

        private static AssetDeleteResult OnWillDeleteAsset(string path, RemoveAssetOptions options)
        {
            if (!path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                return AssetDeleteResult.DidNotDelete;
            }

            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
            var type = script != null ? script.GetClass() : null;

            if (type == null || !IsRegistered(type))
            {
                return AssetDeleteResult.DidNotDelete;
            }

            // Held for the rest of this deletion: Unity calls this once per asset, and deleting a
            // folder full of save types has to exclude all of them at once, not one at a time.
            Doomed.Add(type);
            SaveTypeDiscovery.Exclude(Doomed);

            try
            {
                SaveRegistryGenerator.Generate();
                ImplicitSaveLog.Info(
                    $"'{type.Name}' is being deleted, so it was removed from the generated registry first. " +
                    "Saves that hold it will load without it.");
            }
            finally
            {
                SaveTypeDiscovery.Exclude(null);
            }

            // Never claim the deletion - Unity still has to remove the file itself.
            return AssetDeleteResult.DidNotDelete;
        }

        /// <summary>Whether this type is something the generated registry would name.</summary>
        private static bool IsRegistered(Type type)
        {
            if (typeof(SaveData).IsAssignableFrom(type) || typeof(ISaveMigration).IsAssignableFrom(type))
            {
                return true;
            }

            return Attribute.IsDefined(type, typeof(SaveTypeAttribute), inherit: false);
        }

        [InitializeOnLoadMethod]
        private static void ForgetAfterReload()
        {
            // The domain reloaded, so the deletion is done and these types no longer exist.
            Doomed.Clear();
            SaveTypeDiscovery.Exclude(null);
        }
    }
}
