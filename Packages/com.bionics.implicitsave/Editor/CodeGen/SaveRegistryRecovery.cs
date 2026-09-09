using System.IO;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.Compilation;
using UnityEngine;

namespace ImplicitSave.Editor
{
    /// <summary>
    /// Gets the project out of the one deadlock the generated registry can create.
    /// </summary>
    /// <remarks>
    /// <c>SaveRegistry.g.cs</c> names every save type in plain C#, which is what stops IL2CPP from
    /// stripping them. The cost is that removing a save type breaks that file, and the break is
    /// circular: the generator that would fix it runs after a successful compile, which can no
    /// longer happen. The project sits on an error whose only manual fix is deleting a file the
    /// package tells you not to touch.
    /// <para>
    /// Compilation runs in the CURRENT domain and only reloads once it succeeds, so this callback is
    /// still alive when the failure happens - which is the whole opportunity. It deletes the
    /// generated file rather than trying to repair it: the file is an artifact, the next successful
    /// reload writes it again from what actually exists, and a fresh scan is more trustworthy than
    /// anything patched together from compiler messages.
    /// </para>
    /// <para>
    /// <see cref="SaveTypeDeletionWatcher"/> is the other half. It heads this off when a whole script
    /// is deleted, so no error appears at all; this one covers everything else - a class removed from
    /// a file that still exists, an assembly definition that moved, a rename done by hand.
    /// </para>
    /// </remarks>
    [InitializeOnLoad]
    internal static class SaveRegistryRecovery
    {
        /// <summary>
        /// Survives the domain reload, which is what keeps a file that cannot be fixed from looping
        /// forever between delete and regenerate.
        /// </summary>
        private const string AttemptedKey = "ImplicitSave.RegistryRecoveryAttempted";

        static SaveRegistryRecovery()
        {
            CompilationPipeline.assemblyCompilationFinished -= OnAssemblyCompilationFinished;
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompilationFinished;
        }

        private static void OnAssemblyCompilationFinished(string assemblyPath, CompilerMessage[] messages)
        {
            if (!BlamesTheGeneratedRegistry(messages))
            {
                return;
            }

            if (SessionState.GetBool(AttemptedKey, false))
            {
                // Already tried once this session and the file is still the problem. Saying so is
                // better than deleting it again on every compile.
                Debug.LogError(
                    "[ImplicitSave] The generated registry still does not compile after being rebuilt. " +
                    $"Delete '{SaveRegistryGenerator.RegistryPath}' by hand and let it regenerate. If it comes " +
                    "back broken, the save types it names are worth a look.");
                return;
            }

            // Deferred out of the compilation callback: touching the asset database while a compile
            // is being finalised is asking for trouble. It waits on `update` rather than `delayCall`
            // because a delayCall queued during a FAILED compile was observed never to run - there is
            // no domain reload behind it to flush the queue, and the project stayed broken.
            EditorApplication.update -= RebuildOnNextTick;
            EditorApplication.update += RebuildOnNextTick;
        }

        private static void RebuildOnNextTick()
        {
            EditorApplication.update -= RebuildOnNextTick;
            RebuildFromScratch();
        }

        private static void RebuildFromScratch()
        {
            if (!File.Exists(SaveRegistryGenerator.RegistryPath))
            {
                return;
            }

            SessionState.SetBool(AttemptedKey, true);

            Debug.Log(
                "[ImplicitSave] A save type named in the generated registry no longer exists, which stops the " +
                "project compiling. Rebuilding the registry from what is actually there - no action needed.");

            if (!AssetDatabase.DeleteAsset(SaveRegistryGenerator.RegistryPath))
            {
                // The asset database can refuse while it is busy. The file is what matters, so
                // remove it directly and let the refresh notice.
                File.Delete(SaveRegistryGenerator.RegistryPath);
                File.Delete(SaveRegistryGenerator.RegistryPath + ".meta");
            }

            AssetDatabase.Refresh();

            // The delete alone may not be enough to trigger a compile, and without one the project
            // stays broken until something else happens to change.
            CompilationPipeline.RequestScriptCompilation();
        }

        /// <summary>Whether any error in this batch points at the file the package generates.</summary>
        private static bool BlamesTheGeneratedRegistry(CompilerMessage[] messages)
        {
            if (messages == null)
            {
                return false;
            }

            foreach (var message in messages)
            {
                if (message.type == CompilerMessageType.Error
                    && !string.IsNullOrEmpty(message.file)
                    && message.file.Replace('\\', '/').EndsWith(SaveRegistryGenerator.RegistryPath, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        [DidReloadScripts]
        private static void ForgetAfterASuccessfulCompile()
        {
            // Getting here at all means compilation succeeded, so whatever went wrong is behind us
            // and the next problem deserves its own attempt.
            SessionState.SetBool(AttemptedKey, false);
        }
    }
}
