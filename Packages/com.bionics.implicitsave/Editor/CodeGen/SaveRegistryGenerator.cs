using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Callbacks;
using UnityEngine;

namespace ImplicitSave.Editor
{
    /// <summary>
    /// Writes the generated registry: a plain C# file naming every save type explicitly, plus a
    /// <c>link.xml</c> saying the same thing to the linker.
    /// </summary>
    /// <remarks>
    /// This is what makes the package work in a real build. IL2CPP's managed stripping removes types
    /// nothing references, and a package whose selling point is that you never write a reference
    /// produces exactly those types. The generated file is that missing reference.
    /// <para>
    /// It lives under <c>Assets/</c> rather than in the package because packages are immutable, and
    /// it is meant to be committed: reading it is how a user confirms there is no magic.
    /// </para>
    /// </remarks>
    public static class SaveRegistryGenerator
    {
        /// <summary>Folder the generated files are written to, inside the user's project.</summary>
        public const string OutputFolder = "Assets/ImplicitSave.Generated";

        /// <summary>Path of the generated registry.</summary>
        public const string RegistryPath = OutputFolder + "/SaveRegistry.g.cs";

        /// <summary>Path of the generated linker directives.</summary>
        public const string LinkXmlPath = OutputFolder + "/link.xml";

        /// <summary>
        /// Path of the assembly definition for the generated folder. Only written when every save
        /// type lives in an assembly definition of its own.
        /// </summary>
        public const string AsmdefPath = OutputFolder + "/ImplicitSave.Generated.asmdef";

        /// <summary>
        /// Regenerates the files. Does nothing to disk when the content has not changed, which is
        /// what keeps this from starting a compile loop with itself.
        /// </summary>
        /// <returns><c>true</c> when a file was actually written.</returns>
        public static bool Generate()
        {
            // Only what a build will actually contain: an editor or test assembly is not
            // compiled into a player, so naming its types here produces a file that cannot compile.
            var saveTypes = SaveTypeDiscovery.FindBuildableSaveTypes();
            var subtypes = SaveTypeDiscovery.FindBuildableSubtypes();

            var problems = Validate(saveTypes, subtypes);
            if (problems.Count > 0)
            {
                foreach (var problem in problems)
                {
                    Debug.LogError("[ImplicitSave] " + problem);
                }

                return false;
            }

            Directory.CreateDirectory(OutputFolder);

            var wrote = WriteAssemblyDefinition(saveTypes, subtypes);
            wrote |= WriteIfChanged(RegistryPath, BuildRegistrySource(saveTypes, subtypes));
            wrote |= WriteIfChanged(LinkXmlPath, BuildLinkXml(saveTypes, subtypes));

            if (wrote)
            {
                AssetDatabase.Refresh();
            }

            return wrote;
        }

        /// <summary>
        /// Decides whether the generated folder gets an assembly definition of its own, and writes
        /// or removes it accordingly.
        /// </summary>
        /// <remarks>
        /// The generated file has to be able to name every save type, and which assembly it lands in
        /// decides what it can see.
        /// <list type="bullet">
        /// <item>Every save type in its own assembly definition: give the generated folder an
        /// assembly definition too and reference them explicitly. Cleanest, and it works no matter
        /// how those assemblies are configured.</item>
        /// <item>Any save type in a predefined assembly such as <c>Assembly-CSharp</c>: no assembly
        /// definition, because one cannot reference a predefined assembly. The generated file joins
        /// <c>Assembly-CSharp</c>, which can see everything auto-referenced.</item>
        /// </list>
        /// </remarks>
        private static bool WriteAssemblyDefinition(IReadOnlyList<Type> saveTypes, IReadOnlyList<Type> subtypes)
        {
            var assemblies = CollectAssemblies(saveTypes, subtypes);
            var needsPredefined = false;

            foreach (var assembly in assemblies)
            {
                if (SaveTypeDiscovery.IsPredefinedAssembly(assembly))
                {
                    needsPredefined = true;
                    break;
                }
            }

            if (needsPredefined || assemblies.Count == 0)
            {
                return DeleteIfPresent(AsmdefPath);
            }

            var references = new List<string> { "ImplicitSave.Runtime" };
            references.AddRange(assemblies);

            var json = new StringBuilder();
            json.AppendLine("{");
            json.AppendLine("    \"name\": \"ImplicitSave.Generated\",");
            json.AppendLine("    \"references\": [");

            for (var i = 0; i < references.Count; i++)
            {
                var comma = i < references.Count - 1 ? "," : string.Empty;
                json.AppendLine($"        \"{references[i]}\"{comma}");
            }

            json.AppendLine("    ],");
            json.AppendLine("    \"includePlatforms\": [],");
            json.AppendLine("    \"excludePlatforms\": [],");
            json.AppendLine("    \"autoReferenced\": true,");
            json.AppendLine("    \"noEngineReferences\": false");
            json.AppendLine("}");

            return WriteIfChanged(AsmdefPath, json.ToString());
        }

        private static List<string> CollectAssemblies(IReadOnlyList<Type> saveTypes, IReadOnlyList<Type> subtypes)
        {
            var assemblies = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var type in saveTypes)
            {
                assemblies.Add(type.Assembly.GetName().Name);
            }

            foreach (var type in subtypes)
            {
                assemblies.Add(type.Assembly.GetName().Name);
            }

            return new List<string>(assemblies);
        }

        private static bool DeleteIfPresent(string path)
        {
            if (!File.Exists(path))
            {
                return false;
            }

            AssetDatabase.DeleteAsset(path);
            return true;
        }

        /// <summary>
        /// Problems that must stop a build rather than be warned about, because each one produces a
        /// save that silently loads the wrong data or cannot be created at all.
        /// </summary>
        public static IReadOnlyList<string> Validate(IReadOnlyList<Type> saveTypes, IReadOnlyList<Type> subtypes)
        {
            var problems = new List<string>();
            var seenSaveIds = new Dictionary<string, Type>(StringComparer.Ordinal);
            var seenSubtypeIds = new Dictionary<string, Type>(StringComparer.Ordinal);

            foreach (var type in saveTypes)
            {
                var id = SaveTypeDiscovery.ResolveSaveId(type);
                CheckId(type, id, "SaveId", seenSaveIds, problems);

                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    problems.Add($"'{type.FullName}' has no public parameterless constructor, so it cannot " +
                                 "be created when no save file exists.");
                }
            }

            foreach (var type in subtypes)
            {
                var id = SaveTypeDiscovery.ResolveSubtypeId(type);
                CheckId(type, id, "SaveType", seenSubtypeIds, problems);

                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    problems.Add($"'{type.FullName}' has no public parameterless constructor, so it cannot " +
                                 "be restored from a save file.");
                }
            }

            CheckAssemblyVisibility(saveTypes, subtypes, problems);
            return problems;
        }

        /// <summary>
        /// Catches the one project layout the generated file cannot express: some save types in a
        /// predefined assembly (so the generated folder can have no assembly definition) and others
        /// in an assembly definition with Auto Referenced off (so Assembly-CSharp cannot see them).
        /// </summary>
        private static void CheckAssemblyVisibility(
            IReadOnlyList<Type> saveTypes, IReadOnlyList<Type> subtypes, List<string> problems)
        {
            var assemblies = CollectAssemblies(saveTypes, subtypes);
            var hasPredefined = false;

            foreach (var assembly in assemblies)
            {
                if (SaveTypeDiscovery.IsPredefinedAssembly(assembly))
                {
                    hasPredefined = true;
                    break;
                }
            }

            if (!hasPredefined)
            {
                return;
            }

            foreach (var assembly in assemblies)
            {
                if (SaveTypeDiscovery.IsPredefinedAssembly(assembly) || SaveTypeDiscovery.IsAutoReferenced(assembly))
                {
                    continue;
                }

                problems.Add($"'{assembly}' has Auto Referenced turned off, and this project also keeps save " +
                             "types in a predefined assembly such as Assembly-CSharp. The generated registry " +
                             "cannot see both at once. Turn Auto Referenced on for that assembly definition, or " +
                             "move every save type into assembly definitions.");
            }
        }

        private static void CheckId(
            Type type, string id, string attributeName, Dictionary<string, Type> seen, List<string> problems)
        {
            if (!SaveIdResolver.IsValidId(id, out var reason))
            {
                problems.Add($"[{attributeName}(\"{id}\")] on '{type.FullName}' is not usable: {reason}. " +
                             "Ids become file names, so they are limited to lowercase letters, digits, '_' and '-'.");
                return;
            }

            if (seen.TryGetValue(id, out var other))
            {
                problems.Add($"'{type.FullName}' and '{other.FullName}' both use the id '{id}'. " +
                             "They would overwrite each other - give one of them a different id.");
                return;
            }

            seen[id] = type;
        }

        private static string BuildRegistrySource(IReadOnlyList<Type> saveTypes, IReadOnlyList<Type> subtypes)
        {
            var source = new StringBuilder(2048);

            source.AppendLine("// ---------------------------------------------------------------------------");
            source.AppendLine("// SaveRegistry.g.cs - GENERATED FILE. Do not edit; your changes will be lost.");
            source.AppendLine("//");
            source.AppendLine("// Written by ImplicitSave, which lists every save type in your project here so");
            source.AppendLine("// that IL2CPP's managed stripping cannot delete them from a build. Types that");
            source.AppendLine("// nothing references are removed by the linker, and this package exists so you");
            source.AppendLine("// never have to write those references yourself - this file writes them for you.");
            source.AppendLine("//");
            source.AppendLine("// It regenerates when scripts recompile and again before every build. To force");
            source.AppendLine("// it: Tools > ImplicitSave > Regenerate Registry.");
            source.AppendLine("//");
            source.AppendLine("// Commit this file. Reading it is how you check what the package is doing.");
            source.AppendLine("// ---------------------------------------------------------------------------");
            source.AppendLine();
            source.AppendLine("namespace ImplicitSave.Generated");
            source.AppendLine("{");
            source.AppendLine("    internal static class SaveRegistry");
            source.AppendLine("    {");
            source.AppendLine("        [UnityEngine.RuntimeInitializeOnLoadMethod(");
            source.AppendLine("            UnityEngine.RuntimeInitializeLoadType.BeforeSceneLoad)]");
            source.AppendLine("        internal static void Register()");
            source.AppendLine("        {");

            AppendSection(source, "save roots", saveTypes, isSubtype: false);
            AppendSection(source, "polymorphic subtypes", subtypes, isSubtype: true);

            if (saveTypes.Count == 0 && subtypes.Count == 0)
            {
                source.AppendLine("            // No save types in this project yet. Declare a class deriving from");
                source.AppendLine("            // ImplicitSave.SaveData and this file will fill itself in.");
            }

            source.AppendLine("        }");
            source.AppendLine("    }");
            source.AppendLine("}");

            return source.ToString();
        }

        private static void AppendSection(StringBuilder source, string title, IReadOnlyList<Type> types, bool isSubtype)
        {
            if (types.Count == 0)
            {
                return;
            }

            var entries = new List<string>(types.Count);

            foreach (var type in types)
            {
                var id = isSubtype ? SaveTypeDiscovery.ResolveSubtypeId(type) : SaveTypeDiscovery.ResolveSaveId(type);
                var name = "global::" + type.FullName.Replace('+', '.');
                var method = isSubtype ? "RegisterSubtype" : "Register";

                entries.Add($"            ImplicitSave.SaveTypeRegistry.{method}(" +
                            $"\"{id}\", typeof({name}), () => new {name}());");
            }

            // Sorted so the file has a stable order and a clean diff in Git.
            entries.Sort(StringComparer.Ordinal);

            source.AppendLine($"            // {title}");
            foreach (var entry in entries)
            {
                source.AppendLine(entry);
            }

            source.AppendLine();
        }

        /// <summary>
        /// Tells the linker to keep these types whole. Redundant with the registry on purpose: the
        /// registry keeps the type, this also keeps its fields and constructor under aggressive
        /// stripping.
        /// </summary>
        private static string BuildLinkXml(IReadOnlyList<Type> saveTypes, IReadOnlyList<Type> subtypes)
        {
            var byAssembly = new SortedDictionary<string, SortedSet<string>>(StringComparer.Ordinal);

            foreach (var type in saveTypes)
            {
                AddToAssembly(byAssembly, type);
            }

            foreach (var type in subtypes)
            {
                AddToAssembly(byAssembly, type);
            }

            var xml = new StringBuilder(1024);
            xml.AppendLine("<!--");
            xml.AppendLine("  link.xml - GENERATED FILE. Do not edit.");
            xml.AppendLine();
            xml.AppendLine("  Keeps ImplicitSave's save types, and their fields and constructors, from being");
            xml.AppendLine("  removed by managed code stripping. Regenerated with SaveRegistry.g.cs.");
            xml.AppendLine("-->");
            xml.AppendLine("<linker>");

            foreach (var assembly in byAssembly)
            {
                xml.AppendLine($"  <assembly fullname=\"{assembly.Key}\">");

                foreach (var typeName in assembly.Value)
                {
                    xml.AppendLine($"    <type fullname=\"{typeName}\" preserve=\"all\" />");
                }

                xml.AppendLine("  </assembly>");
            }

            xml.AppendLine("</linker>");
            return xml.ToString();
        }

        private static void AddToAssembly(SortedDictionary<string, SortedSet<string>> byAssembly, Type type)
        {
            var assembly = type.Assembly.GetName().Name;

            if (!byAssembly.TryGetValue(assembly, out var types))
            {
                types = new SortedSet<string>(StringComparer.Ordinal);
                byAssembly[assembly] = types;
            }

            types.Add(type.FullName);
        }

        /// <summary>
        /// Writes only when the content differs. Rewriting an identical file would trigger a
        /// recompile, which would trigger this generator, which would rewrite the file.
        /// </summary>
        private static bool WriteIfChanged(string path, string content)
        {
            if (File.Exists(path) && File.ReadAllText(path) == content)
            {
                return false;
            }

            File.WriteAllText(path, content);
            return true;
        }

        [MenuItem("Tools/ImplicitSave/Regenerate Registry")]
        private static void RegenerateFromMenu()
        {
            if (Generate())
            {
                Debug.Log($"[ImplicitSave] Regenerated {RegistryPath}.");
            }
            else
            {
                Debug.Log("[ImplicitSave] The registry was already up to date.");
            }
        }

        [DidReloadScripts]
        private static void RegenerateAfterCompile()
        {
            // Keeps the file current while developing, so the promise that declaring a class is
            // enough holds without anyone remembering to press a menu item.
            Generate();
        }

        /// <summary>
        /// Regenerates before a build, and refuses to build on a problem the runtime could not
        /// report in a useful way.
        /// </summary>
        internal sealed class BuildHook : IPreprocessBuildWithReport
        {
            public int callbackOrder => -1000;

            public void OnPreprocessBuild(BuildReport report)
            {
                var problems = Validate(
                    SaveTypeDiscovery.FindBuildableSaveTypes(), SaveTypeDiscovery.FindBuildableSubtypes());

                if (problems.Count > 0)
                {
                    // Failing the build is the kind thing to do. Each of these produces a save that
                    // loads the wrong data or cannot be created, and the player finds out, not you.
                    throw new BuildFailedException(
                        "[ImplicitSave] " + string.Join("\n[ImplicitSave] ", problems));
                }

                Generate();
            }
        }
    }
}
