using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Compilation;

namespace ImplicitSave.Editor
{
    /// <summary>
    /// Finds save types by scanning the project. This is the editor's half of type discovery; a
    /// build uses the generated registry instead.
    /// </summary>
    /// <remarks>
    /// <c>TypeCache</c> is maintained by the editor and is effectively free to query, so the editor
    /// never needs the generated file to be up to date. That is deliberate: having to regenerate
    /// after adding a class would break the promise that declaring one is enough.
    /// <para>
    /// Two lists matter here and they are not the same. What exists <b>in the editor</b> includes
    /// types in editor-only and test assemblies. What can go <b>into a build</b> excludes them,
    /// because those assemblies are not compiled into a player - naming their types in the generated
    /// registry produces a file that will not compile.
    /// </para>
    /// </remarks>
    [InitializeOnLoad]
    public static class SaveTypeDiscovery
    {
        private static HashSet<string> _playerAssemblyNames;

        static SaveTypeDiscovery()
        {
            // Installed once per domain load, so the registry can fill itself on first use - in
            // edit mode and in play mode alike.
            SaveTypeRegistry.EditorDiscovery = PopulateRegistry;
        }

        /// <summary>Every concrete save root the editor can see, including editor-only ones.</summary>
        public static IReadOnlyList<Type> FindSaveTypes()
        {
            return Collect(TypeCache.GetTypesDerivedFrom<SaveData>(), buildableOnly: false);
        }

        /// <summary>Every concrete type marked with <see cref="SaveTypeAttribute"/>.</summary>
        public static IReadOnlyList<Type> FindSubtypes()
        {
            return Collect(TypeCache.GetTypesWithAttribute<SaveTypeAttribute>(), buildableOnly: false);
        }

        /// <summary>Save roots that will exist in a build. This is what the generator writes out.</summary>
        public static IReadOnlyList<Type> FindBuildableSaveTypes()
        {
            return Collect(TypeCache.GetTypesDerivedFrom<SaveData>(), buildableOnly: true);
        }

        /// <summary>Subtypes that will exist in a build.</summary>
        public static IReadOnlyList<Type> FindBuildableSubtypes()
        {
            return Collect(TypeCache.GetTypesWithAttribute<SaveTypeAttribute>(), buildableOnly: true);
        }

        /// <summary>Every migration the editor can see.</summary>
        public static IReadOnlyList<Type> FindMigrations()
        {
            return Collect(TypeCache.GetTypesDerivedFrom<ISaveMigration>(), buildableOnly: false);
        }

        /// <summary>Migrations that will exist in a build.</summary>
        public static IReadOnlyList<Type> FindBuildableMigrations()
        {
            return Collect(TypeCache.GetTypesDerivedFrom<ISaveMigration>(), buildableOnly: true);
        }

        /// <summary>
        /// Save types the editor can see that a build cannot, with the reason. Surfacing these is
        /// how a type quietly missing from a player build gets noticed before release.
        /// </summary>
        public static IReadOnlyList<KeyValuePair<Type, string>> FindExcludedTypes()
        {
            var excluded = new List<KeyValuePair<Type, string>>();

            foreach (var type in FindSaveTypes())
            {
                if (TryGetExclusionReason(type, out var reason))
                {
                    excluded.Add(new KeyValuePair<Type, string>(type, reason));
                }
            }

            excluded.Sort((a, b) => CompareByName(a.Key, b.Key));
            return excluded;
        }

        /// <summary>Fills the runtime registry from the project scan, for play mode in the editor.</summary>
        public static void PopulateRegistry()
        {
            foreach (var type in FindSaveTypes())
            {
                var captured = type;
                SaveTypeRegistry.Add(new SaveTypeEntry(
                    ResolveSaveId(type), type, isSubtype: false, SaveTypeOrigin.EditorScan,
                    () => Activator.CreateInstance(captured)));
            }

            foreach (var type in FindSubtypes())
            {
                var captured = type;
                SaveTypeRegistry.Add(new SaveTypeEntry(
                    ResolveSubtypeId(type), type, isSubtype: true, SaveTypeOrigin.EditorScan,
                    () => Activator.CreateInstance(captured)));

                foreach (var previous in ResolvePreviousIds(type))
                {
                    SaveTypeRegistry.RegisterPreviousId(previous, type);
                }
            }

            foreach (var type in FindMigrations())
            {
                if (type.GetConstructor(Type.EmptyTypes) == null)
                {
                    // Reported properly by the generator's validation; here it just cannot be built.
                    continue;
                }

                SaveTypeRegistry.RegisterMigration((ISaveMigration)Activator.CreateInstance(type));
            }
        }

        /// <summary>The id a save root will be stored under.</summary>
        public static string ResolveSaveId(Type type)
        {
            var attribute = (SaveIdAttribute)Attribute.GetCustomAttribute(type, typeof(SaveIdAttribute), false);
            return attribute != null ? attribute.Id : SaveIdResolver.ToSnakeCase(type.Name);
        }

        /// <summary>Ids this subtype used to be written under, in declaration order.</summary>
        public static IReadOnlyList<string> ResolvePreviousIds(Type type)
        {
            var attribute = (SaveTypeAttribute)Attribute.GetCustomAttribute(type, typeof(SaveTypeAttribute), false);

            if (attribute == null || attribute.PreviousIds == null)
            {
                return Array.Empty<string>();
            }

            var ids = new List<string>();

            foreach (var id in attribute.PreviousIds)
            {
                if (!string.IsNullOrEmpty(id) && !string.Equals(id, attribute.Id, StringComparison.Ordinal))
                {
                    ids.Add(id);
                }
            }

            return ids;
        }

        /// <summary>The <c>$t</c> value a subtype will be stored under.</summary>
        public static string ResolveSubtypeId(Type type)
        {
            var attribute = (SaveTypeAttribute)Attribute.GetCustomAttribute(type, typeof(SaveTypeAttribute), false);
            return attribute != null ? attribute.Id : SaveIdResolver.ToSnakeCase(type.Name);
        }

        /// <summary>Whether the type lives in an assembly that is compiled into a build.</summary>
        public static bool IsInPlayerAssembly(Type type)
        {
            return TryGetExclusionReason(type, out _) == false;
        }

        /// <summary>
        /// Why a save type cannot go into the generated registry, or <c>false</c> when it can.
        /// </summary>
        public static bool TryGetExclusionReason(Type type, out string reason)
        {
            var assemblyName = type.Assembly.GetName().Name;

            if (!PlayerAssemblyNames.Contains(assemblyName))
            {
                reason = $"'{assemblyName}' is an editor-only assembly and is not compiled into a build, " +
                         "so this type works in the editor and does not exist in a player.";
                return true;
            }

            var info = GetAssemblyInfo(assemblyName);

            if (info.IsTestAssembly)
            {
                reason = $"'{assemblyName}' is a test assembly - it is gated behind UNITY_INCLUDE_TESTS and " +
                         "is not part of a normal build.";
                return true;
            }

            reason = null;
            return false;
        }

        /// <summary>Whether the assembly is a predefined one such as <c>Assembly-CSharp</c>.</summary>
        public static bool IsPredefinedAssembly(string assemblyName)
        {
            return GetAssemblyInfo(assemblyName).IsPredefined;
        }

        /// <summary>
        /// Whether <c>Assembly-CSharp</c> can see this assembly. An assembly definition with Auto
        /// Referenced turned off is invisible to it, which matters because that is where the
        /// generated registry lands when it cannot have an assembly definition of its own.
        /// </summary>
        public static bool IsAutoReferenced(string assemblyName)
        {
            return GetAssemblyInfo(assemblyName).AutoReferenced;
        }

        private readonly struct AssemblyInfo
        {
            internal readonly bool IsPredefined;
            internal readonly bool IsTestAssembly;
            internal readonly bool AutoReferenced;

            internal AssemblyInfo(bool isPredefined, bool isTestAssembly, bool autoReferenced)
            {
                IsPredefined = isPredefined;
                IsTestAssembly = isTestAssembly;
                AutoReferenced = autoReferenced;
            }
        }

        private static Dictionary<string, AssemblyInfo> _assemblyInfo;

        private static AssemblyInfo GetAssemblyInfo(string assemblyName)
        {
            _assemblyInfo = _assemblyInfo ?? new Dictionary<string, AssemblyInfo>(StringComparer.Ordinal);

            if (_assemblyInfo.TryGetValue(assemblyName, out var cached))
            {
                return cached;
            }

            var info = ReadAssemblyInfo(assemblyName);
            _assemblyInfo[assemblyName] = info;
            return info;
        }

        /// <summary>
        /// Reads the facts that matter straight out of the .asmdef. Unity exposes no API for these,
        /// and guessing from the assembly name would be wrong for anyone's project but this one.
        /// </summary>
        private static AssemblyInfo ReadAssemblyInfo(string assemblyName)
        {
            var path = CompilationPipeline.GetAssemblyDefinitionFilePathFromAssemblyName(assemblyName);

            if (string.IsNullOrEmpty(path))
            {
                // Assembly-CSharp and friends: always in a build, always visible.
                return new AssemblyInfo(isPredefined: true, isTestAssembly: false, autoReferenced: true);
            }

            var isTest = false;
            var autoReferenced = true;

            try
            {
                var json = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));

                if (json["defineConstraints"] is Newtonsoft.Json.Linq.JArray constraints)
                {
                    foreach (var constraint in constraints)
                    {
                        if (string.Equals((string)constraint, "UNITY_INCLUDE_TESTS", StringComparison.Ordinal))
                        {
                            isTest = true;
                        }
                    }
                }

                var flag = json["autoReferenced"];
                if (flag != null && flag.Type == Newtonsoft.Json.Linq.JTokenType.Boolean)
                {
                    autoReferenced = (bool)flag;
                }
            }
            catch (Exception e)
            {
                // A malformed asmdef is the project's problem, not ours. Assume the permissive
                // answer and let the compiler report it.
                UnityEngine.Debug.LogWarning($"[ImplicitSave] Could not read '{path}': {e.Message}");
            }

            return new AssemblyInfo(isPredefined: false, isTestAssembly: isTest, autoReferenced: autoReferenced);
        }

        private static ICollection<Type> _excluded;

        /// <summary>
        /// Hides types from every lookup below, for the moment between "this script is about to be
        /// deleted" and the deletion actually happening.
        /// </summary>
        /// <remarks>
        /// <see cref="TypeCache"/> still reports a type whose file is on its way out, because the
        /// assemblies have not been rebuilt yet. Regenerating the registry in that window would
        /// write the doomed type straight back in, which is the opposite of the point. Pass
        /// <c>null</c> to clear.
        /// </remarks>
        internal static void Exclude(ICollection<Type> types)
        {
            _excluded = types != null && types.Count > 0 ? types : null;
        }

        private static IReadOnlyList<Type> Collect(IEnumerable<Type> candidates, bool buildableOnly)
        {
            var types = new List<Type>();

            foreach (var type in candidates)
            {
                // Abstract and generic types have nothing to instantiate. That is not an error -
                // they are simply not saves.
                if (type.IsAbstract || type.IsGenericTypeDefinition || type.IsInterface)
                {
                    continue;
                }

                if (buildableOnly && !IsInPlayerAssembly(type))
                {
                    continue;
                }

                if (_excluded != null && _excluded.Contains(type))
                {
                    continue;
                }

                types.Add(type);
            }

            types.Sort(CompareByName);
            return types;
        }

        private static HashSet<string> PlayerAssemblyNames
        {
            get
            {
                if (_playerAssemblyNames != null)
                {
                    return _playerAssemblyNames;
                }

                // Rebuilt once per domain load, which is also the only time it can change.
                _playerAssemblyNames = new HashSet<string>(StringComparer.Ordinal);

                foreach (var assembly in CompilationPipeline.GetAssemblies(AssembliesType.Player))
                {
                    _playerAssemblyNames.Add(assembly.name);
                }

                return _playerAssemblyNames;
            }
        }

        private static int CompareByName(Type a, Type b)
        {
            return string.CompareOrdinal(a.FullName, b.FullName);
        }
    }
}
