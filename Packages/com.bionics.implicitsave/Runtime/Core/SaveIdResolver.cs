using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ImplicitSave
{
    /// <summary>
    /// Resolves the stable on-disk id of a save type: the <see cref="SaveIdAttribute"/> value when
    /// present, otherwise the type name in snake_case with a warning.
    /// </summary>
    /// <remarks>
    /// This is the single place that decides what a save file is called. The type registry of a
    /// later phase builds its id map on top of it rather than repeating the rules.
    /// </remarks>
    internal static class SaveIdResolver
    {
        private static readonly Dictionary<Type, string> Cache = new Dictionary<Type, string>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            // Static state survives play sessions when Disable Domain Reload is on.
            Cache.Clear();
        }

        /// <summary>Returns the save id for <paramref name="type"/>, caching the answer.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="type"/> is null.</exception>
        /// <exception cref="SaveException">The declared id is not usable as a file name.</exception>
        internal static string Resolve(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (Cache.TryGetValue(type, out var cached))
            {
                return cached;
            }

            var id = ResolveUncached(type);
            Cache[type] = id;
            return id;
        }

        private static string ResolveUncached(Type type)
        {
            var attribute = (SaveIdAttribute)Attribute.GetCustomAttribute(type, typeof(SaveIdAttribute), false);
            if (attribute == null)
            {
                var fallback = ToSnakeCase(type.Name);
                ImplicitSaveLog.Warning(
                    $"'{type.Name}' has no [SaveId]. Falling back to '{fallback}'. Renaming the class " +
                    $"would then orphan every save file already written - declare [SaveId(\"{fallback}\")] to pin it.");
                return fallback;
            }

            var id = attribute.Id;
            if (!IsValidId(id, out var reason))
            {
                throw new SaveException(
                    $"[SaveId(\"{id}\")] on '{type.Name}' is not a usable save id: {reason}. " +
                    "Save ids become file names, so they are limited to lowercase letters, digits, '_' and '-'.");
            }

            return id;
        }

        /// <summary>
        /// Checks that an id is safe to use as a file name. Rejecting path separators here is what
        /// keeps a save id from reaching outside its profile folder.
        /// </summary>
        internal static bool IsValidId(string id, out string reason)
        {
            if (string.IsNullOrEmpty(id))
            {
                reason = "it is empty";
                return false;
            }

            foreach (var c in id)
            {
                var allowed = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!allowed)
                {
                    reason = $"'{c}' is not allowed";
                    return false;
                }
            }

            reason = null;
            return true;
        }

        /// <summary>
        /// Converts a type name to snake_case: <c>ItemsSaveData</c> becomes <c>items_save_data</c>,
        /// <c>UIStateSaveData</c> becomes <c>ui_state_save_data</c>.
        /// </summary>
        internal static string ToSnakeCase(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
            {
                return typeName;
            }

            var builder = new StringBuilder(typeName.Length + 8);

            for (var i = 0; i < typeName.Length; i++)
            {
                var current = typeName[i];

                if (!char.IsUpper(current))
                {
                    builder.Append(char.IsLetterOrDigit(current) ? current : '_');
                    continue;
                }

                // Break before an uppercase letter that starts a new word: either it follows a
                // lowercase character (Items|Save), or it starts a word after an acronym (UI|State).
                var previous = i > 0 ? typeName[i - 1] : '\0';
                var next = i + 1 < typeName.Length ? typeName[i + 1] : '\0';
                var startsWord = i > 0 && (!char.IsUpper(previous) || (char.IsUpper(previous) && char.IsLower(next)));

                if (startsWord && builder.Length > 0 && builder[builder.Length - 1] != '_')
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(current));
            }

            return builder.ToString();
        }
    }
}
