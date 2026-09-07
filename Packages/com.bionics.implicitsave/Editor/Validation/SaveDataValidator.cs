using System;
using System.Collections.Generic;
using System.Reflection;
using ImplicitSave.Serialization;
using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace ImplicitSave.Editor
{
    /// <summary>
    /// Checks save types against Unity's serializable subset and reports anything that would be
    /// dropped, so the author finds out at compile time instead of from a player.
    /// </summary>
    /// <remarks>
    /// The failure this exists to prevent is silence. A property, a plain <c>Dictionary</c> or a
    /// field nested too deep does not throw and does not warn - it just is not there after a
    /// reload, usually noticed weeks later.
    /// <para>
    /// Editor only. It reads its rules from <see cref="UnitySerializationRules"/>, the same source
    /// the serializer uses, so the two can never disagree about what counts.
    /// </para>
    /// </remarks>
    public static class SaveDataValidator
    {
        private const BindingFlags MemberLookup =
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

        /// <summary>Every concrete save type in the project.</summary>
        public static IReadOnlyList<Type> FindSaveTypes()
        {
            var types = new List<Type>();

            foreach (var type in TypeCache.GetTypesDerivedFrom<SaveData>())
            {
                if (!type.IsAbstract && !type.IsGenericTypeDefinition)
                {
                    types.Add(type);
                }
            }

            types.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
            return types;
        }

        /// <summary>Validates every save type in the project.</summary>
        public static IReadOnlyList<ValidationIssue> ValidateAll()
        {
            var issues = new List<ValidationIssue>();

            foreach (var type in FindSaveTypes())
            {
                issues.AddRange(Validate(type));
            }

            return issues;
        }

        /// <summary>Validates one save type.</summary>
        public static IReadOnlyList<ValidationIssue> Validate(Type saveType)
        {
            if (saveType == null)
            {
                throw new ArgumentNullException(nameof(saveType));
            }

            var issues = new List<ValidationIssue>();

            ValidateIdentity(saveType, issues);
            ValidateMembers(saveType, issues);
            ValidateNestingDepth(saveType, issues);

            return issues;
        }

        private static void ValidateIdentity(Type saveType, List<ValidationIssue> issues)
        {
            if (!Attribute.IsDefined(saveType, typeof(SaveIdAttribute), inherit: false))
            {
                issues.Add(new ValidationIssue(saveType, null, ValidationSeverity.Warning,
                    "has no [SaveId]. The file name falls back to the type name, so renaming the class " +
                    "would orphan every save already on a player's disk."));
            }

            if (saveType.GetConstructor(Type.EmptyTypes) == null)
            {
                issues.Add(new ValidationIssue(saveType, null, ValidationSeverity.Error,
                    "has no public parameterless constructor, so it cannot be created when no save file exists."));
            }
        }

        private static void ValidateMembers(Type saveType, List<ValidationIssue> issues)
        {
            for (var type = saveType; type != null && type != typeof(SaveData) && type != typeof(object); type = type.BaseType)
            {
                foreach (var property in type.GetProperties(MemberLookup))
                {
                    // A get-only property computed from fields is normal and fine. One with a setter
                    // reads like stored state, and will not be stored.
                    if (property.CanWrite && property.GetIndexParameters().Length == 0)
                    {
                        issues.Add(new ValidationIssue(saveType, property.Name, ValidationSeverity.Warning,
                            "is a property, and properties are never saved. Make it a field, or compute it " +
                            "from one."));
                    }
                }

                foreach (var field in type.GetFields(MemberLookup))
                {
                    ValidateField(saveType, field, issues);
                }
            }
        }

        private static void ValidateField(Type saveType, FieldInfo field, List<ValidationIssue> issues)
        {
            if (field.IsStatic)
            {
                return;
            }

            var isIntentionallyExcluded =
                Attribute.IsDefined(field, typeof(NonSerializedAttribute), inherit: false) || field.IsNotSerialized;

            if (isIntentionallyExcluded)
            {
                return;
            }

            // [JsonIgnore] on its own is the one case where the two serializers would disagree:
            // Unity keeps the field, Newtonsoft drops it. The editor would show a value the file
            // does not have.
            if (Attribute.IsDefined(field, typeof(JsonIgnoreAttribute), inherit: false))
            {
                issues.Add(new ValidationIssue(saveType, field.Name, ValidationSeverity.Warning,
                    "is marked [JsonIgnore]. ImplicitSave follows Unity's rules, so the field is still saved. " +
                    "Use [NonSerialized] to keep it out of the file."));
            }

            if (UnitySerializationRules.IsSerializedByUnity(field, out var reason))
            {
                return;
            }

            // A private field with no attribute is almost always deliberate, so it stays quiet.
            if (!field.IsPublic && !Attribute.IsDefined(field, typeof(SerializeField), inherit: false))
            {
                return;
            }

            var severity = UnitySerializationRules.IsPlainDictionary(field.FieldType)
                ? ValidationSeverity.Error
                : ValidationSeverity.Warning;

            issues.Add(new ValidationIssue(saveType, field.Name, severity,
                $"will not be saved because {reason}."));
        }

        /// <summary>
        /// Walks the nested plain classes and reports anything past Unity's depth limit. Unity
        /// truncates there without a word, which is the reason this check exists at all.
        /// </summary>
        private static void ValidateNestingDepth(Type saveType, List<ValidationIssue> issues)
        {
            var visiting = new HashSet<Type>();
            WalkDepth(saveType, saveType, null, 0, visiting, issues);
        }

        private static void WalkDepth(
            Type saveType, Type current, string path, int depth, HashSet<Type> visiting, List<ValidationIssue> issues)
        {
            if (depth > UnitySerializationRules.MaxNestingDepth)
            {
                issues.Add(new ValidationIssue(saveType, path, ValidationSeverity.Warning,
                    $"is nested deeper than {UnitySerializationRules.MaxNestingDepth} levels. Unity stops " +
                    "serializing there without reporting anything, so the data below is lost. Flatten the shape."));
                return;
            }

            // A type that contains itself is a cycle, not depth. It is reported elsewhere; here it
            // just must not loop forever.
            if (!visiting.Add(current))
            {
                return;
            }

            foreach (var field in UnitySerializationRules.GetSerializedFields(current))
            {
                var elementType = GetElementType(field.FieldType);
                var childPath = string.IsNullOrEmpty(path) ? field.Name : path + "." + field.Name;

                if (IsLeaf(elementType))
                {
                    continue;
                }

                WalkDepth(saveType, elementType, childPath, depth + 1, visiting, issues);
            }

            visiting.Remove(current);
        }

        private static Type GetElementType(Type type)
        {
            if (type.IsArray)
            {
                return type.GetElementType();
            }

            if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
            {
                return type.GetGenericArguments()[0];
            }

            return type;
        }

        private static bool IsLeaf(Type type)
        {
            return type == null || type.IsPrimitive || type.IsEnum || type == typeof(string);
        }

        [MenuItem("Tools/ImplicitSave/Validate Save Types")]
        private static void ValidateAllFromMenu()
        {
            var issues = ValidateAll();
            var typeCount = FindSaveTypes().Count;

            if (issues.Count == 0)
            {
                Debug.Log($"[ImplicitSave] {typeCount} save type(s) checked, nothing to report.");
                return;
            }

            ReportToConsole(issues);
        }

        [UnityEditor.Callbacks.DidReloadScripts]
        private static void ValidateAfterCompile()
        {
            // Finding out at compile time is the whole point - a warning that only appears when the
            // user goes looking is a warning nobody reads.
            var issues = ValidateAll();
            if (issues.Count > 0)
            {
                ReportToConsole(issues);
            }
        }

        private static void ReportToConsole(IReadOnlyList<ValidationIssue> issues)
        {
            foreach (var issue in issues)
            {
                var line = "[ImplicitSave] " + issue;

                switch (issue.Severity)
                {
                    case ValidationSeverity.Error:
                        Debug.LogError(line);
                        break;
                    case ValidationSeverity.Warning:
                        Debug.LogWarning(line);
                        break;
                    default:
                        Debug.Log(line);
                        break;
                }
            }
        }
    }
}
