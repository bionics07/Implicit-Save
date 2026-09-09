using System;
using System.Collections.Generic;
using System.Reflection;
using ImplicitSave.Serialization;
using UnityEditor;
using UnityEngine;

namespace ImplicitSave.Editor
{
    /// <summary>
    /// Draws <c>[SerializeReference]</c> fields with a type picker, so a save holding "some kind of
    /// weapon" can be edited without writing an inspector for it.
    /// </summary>
    /// <remarks>
    /// Unity's own inspector draws a picker for these too, and this deliberately replaces it. Its
    /// picker offers every type assignable to the field, including ones with no
    /// <see cref="SaveTypeAttribute"/> - and a value of such a type cannot be written to a save file
    /// at all, because there is no id to put in <c>$t</c>. Offering a choice that fails on the next
    /// save is worse than not offering it, so this one is sourced from the registry: what you can
    /// pick here is exactly what can be stored.
    /// </remarks>
    internal static class SubtypePicker
    {
        private const string NoneLabel = "None";

        /// <summary>
        /// Draws a property, handling managed references at any depth and falling back to Unity's
        /// drawing for everything else.
        /// </summary>
        internal static void DrawProperty(SerializedProperty property)
        {
            if (property.propertyType == SerializedPropertyType.ManagedReference)
            {
                DrawManagedReference(property);
                return;
            }

            if (IsManagedReferenceList(property))
            {
                DrawList(property);
                return;
            }

            // Anything else keeps the drawer the user declared for it. That is the reason this
            // window goes through SerializedObject in the first place.
            EditorGUILayout.PropertyField(property, true);
        }

        /// <summary>A list or array whose elements are stored by reference.</summary>
        private static bool IsManagedReferenceList(SerializedProperty property)
        {
            if (!property.isArray || property.propertyType == SerializedPropertyType.String)
            {
                return false;
            }

            // arrayElementType reads "managedReference<Weapon>" for these, and nothing else produces
            // that prefix.
            return property.arrayElementType != null
                   && property.arrayElementType.StartsWith("managedReference", StringComparison.Ordinal);
        }

        private static void DrawManagedReference(SerializedProperty property)
        {
            var fieldType = ResolveFieldType(property);

            using (new EditorGUILayout.HorizontalScope())
            {
                var hasValue = !string.IsNullOrEmpty(property.managedReferenceFullTypename);

                if (hasValue)
                {
                    property.isExpanded = EditorGUILayout.Foldout(property.isExpanded, property.displayName, true);
                }
                else
                {
                    EditorGUILayout.LabelField(property.displayName);
                }

                DrawTypeButton(property, fieldType);
            }

            if (!property.isExpanded || string.IsNullOrEmpty(property.managedReferenceFullTypename))
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                foreach (var child in DirectChildren(property))
                {
                    DrawProperty(child);
                }
            }
        }

        private static void DrawTypeButton(SerializedProperty property, Type fieldType)
        {
            var current = CurrentType(property);
            var label = current == null ? NoneLabel : current.Name;

            // The id belongs in the tooltip, not the label. On screen it repeated the same word in
            // snake_case next to every entry, which crowded the row without telling anyone anything
            // they could not already see - and it is exactly the kind of detail you want ON DEMAND,
            // at the moment you go looking for it in the file.
            var tooltip = current == null
                ? "Nothing stored here. Pick a type to create one."
                : $"Stored as '{IdOf(current)}' in the file, so renaming the class is safe.";

            if (!GUILayout.Button(new GUIContent(label, tooltip), EditorStyles.popup, GUILayout.Width(160f)))
            {
                return;
            }

            ShowMenu(property, fieldType, current);
        }

        private static void ShowMenu(SerializedProperty property, Type fieldType, Type current)
        {
            var menu = new GenericMenu();

            // Captured now: the property object is reused by the iterator, so the menu callback -
            // which runs on a later event - has to work from a copy and its own path.
            var target = property.serializedObject;
            var path = property.propertyPath;

            menu.AddItem(new GUIContent(NoneLabel), current == null, () => Assign(target, path, null));
            menu.AddSeparator(string.Empty);

            var options = AssignableTypes(fieldType);

            if (options.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent(fieldType == null
                    ? "No types available"
                    : $"No type with [SaveType] derives from {fieldType.Name}"));
            }

            foreach (var type in options)
            {
                var captured = type;
                menu.AddItem(new GUIContent(MenuLabel(captured, options)), captured == current,
                    () => Assign(target, path, captured));
            }

            menu.ShowAsContext();
        }

        /// <summary>Every registry subtype that fits this field, ordered by name.</summary>
        internal static List<Type> AssignableTypes(Type fieldType)
        {
            var types = new List<Type>();

            if (fieldType == null)
            {
                return types;
            }

            foreach (var entry in SaveTypeRegistry.GetAll())
            {
                if (entry.IsSubtype && fieldType.IsAssignableFrom(entry.Type))
                {
                    types.Add(entry.Type);
                }
            }

            types.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            return types;
        }

        /// <summary>
        /// Puts a new instance of <paramref name="type"/> in the field, keeping whatever the old
        /// value and the new one have in common.
        /// </summary>
        private static void Assign(SerializedObject target, string path, Type type)
        {
            target.Update();

            var property = target.FindProperty(path);

            if (property == null)
            {
                return;
            }

            var previous = property.managedReferenceValue;
            var value = type == null ? null : SaveTypeRegistry.Create(IdOf(type));

            if (type != null && value == null)
            {
                ImplicitSaveLog.Error($"'{type.FullName}' is in the registry but could not be created.");
                return;
            }

            if (value != null && previous != null)
            {
                CopyCommonFields(previous, value);
            }

            Undo.RegisterCompleteObjectUndo(target.targetObject, "Change Save Subtype");
            property.managedReferenceValue = value;
            property.isExpanded = true;
            target.ApplyModifiedProperties();
        }

        /// <summary>
        /// Copies fields the two types share by name and type.
        /// </summary>
        /// <remarks>
        /// Switching a weapon from a bow to a staff should not silently wipe its name and damage -
        /// those live on the shared base and mean the same thing in both. Fields that exist on only
        /// one side are dropped, because there is nowhere to put them.
        /// </remarks>
        internal static void CopyCommonFields(object from, object to)
        {
            var sources = new Dictionary<string, FieldInfo>(StringComparer.Ordinal);

            foreach (var field in UnitySerializationRules.GetSerializedFields(from.GetType()))
            {
                sources[field.Name] = field;
            }

            foreach (var field in UnitySerializationRules.GetSerializedFields(to.GetType()))
            {
                if (sources.TryGetValue(field.Name, out var source)
                    && field.FieldType.IsAssignableFrom(source.FieldType))
                {
                    field.SetValue(to, source.GetValue(from));
                }
            }
        }

        private static void DrawList(SerializedProperty property)
        {
            property.isExpanded = EditorGUILayout.Foldout(property.isExpanded, property.displayName, true);

            if (!property.isExpanded)
            {
                return;
            }

            using (new EditorGUI.IndentLevelScope())
            {
                var removeAt = -1;

                for (var i = 0; i < property.arraySize; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        using (new EditorGUILayout.VerticalScope())
                        {
                            DrawProperty(property.GetArrayElementAtIndex(i));
                        }

                        if (GUILayout.Button(new GUIContent("-", "Remove this entry."),
                                GUILayout.Width(22f), GUILayout.Height(18f)))
                        {
                            removeAt = i;
                        }
                    }
                }

                if (removeAt >= 0)
                {
                    RemoveAt(property, removeAt);
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();

                    if (GUILayout.Button(new GUIContent("Add", "Add an empty entry, then pick its type."),
                            GUILayout.Width(60f)))
                    {
                        AddEmpty(property);
                    }
                }
            }
        }

        /// <summary>
        /// Appends an entry that is genuinely empty.
        /// </summary>
        /// <remarks>
        /// Unity's own insert copies the entry above it, which on a by-reference list means the new
        /// entry SHARES the previous one's object: editing either changes both, and saving writes
        /// two copies that then load back as two separate things. Unity 6 already inserts null here,
        /// so this mostly stands as a guarantee rather than a correction - but it is the kind of
        /// guarantee that is cheap to keep and expensive to discover missing.
        /// </remarks>
        private static void AddEmpty(SerializedProperty property)
        {
            property.arraySize++;

            var added = property.GetArrayElementAtIndex(property.arraySize - 1);

            if (added.propertyType == SerializedPropertyType.ManagedReference)
            {
                added.managedReferenceValue = null;
            }

            property.serializedObject.ApplyModifiedProperties();
        }

        private static void RemoveAt(SerializedProperty property, int index)
        {
            // On a by-reference list DeleteArrayElementAtIndex nulls the entry instead of removing
            // it, so the null has to be deleted a second time.
            property.DeleteArrayElementAtIndex(index);

            if (index < property.arraySize
                && property.GetArrayElementAtIndex(index).propertyType == SerializedPropertyType.ManagedReference
                && string.IsNullOrEmpty(property.GetArrayElementAtIndex(index).managedReferenceFullTypename))
            {
                property.DeleteArrayElementAtIndex(index);
            }

            property.serializedObject.ApplyModifiedProperties();
        }

        /// <summary>Walks the direct children of a property, without descending into them.</summary>
        private static IEnumerable<SerializedProperty> DirectChildren(SerializedProperty property)
        {
            var iterator = property.Copy();
            var end = property.GetEndProperty();
            var enterChildren = true;

            while (iterator.NextVisible(enterChildren) && !SerializedProperty.EqualContents(iterator, end))
            {
                enterChildren = false;
                yield return iterator.Copy();
            }
        }

        /// <summary>The concrete type currently stored, or null.</summary>
        private static Type CurrentType(SerializedProperty property)
        {
            return ParseTypename(property.managedReferenceFullTypename);
        }

        /// <summary>The type the field is declared as, which bounds what may be picked.</summary>
        private static Type ResolveFieldType(SerializedProperty property)
        {
            return ParseTypename(property.managedReferenceFieldTypename);
        }

        /// <summary>
        /// Unity reports these as "<c>assembly Namespace.Type</c>", which is not a name any
        /// reflection API accepts.
        /// </summary>
        private static Type ParseTypename(string typename)
        {
            if (string.IsNullOrEmpty(typename))
            {
                return null;
            }

            var split = typename.IndexOf(' ');

            if (split <= 0)
            {
                return null;
            }

            var assembly = typename.Substring(0, split);
            var fullName = typename.Substring(split + 1);

            return Type.GetType($"{fullName}, {assembly}");
        }

        private static string IdOf(Type type)
        {
            return SaveTypeRegistry.TryGetId(type, out var id) ? id : null;
        }

        /// <summary>
        /// The class name, qualified only when the short name alone would be ambiguous.
        /// </summary>
        /// <remarks>
        /// A menu has no tooltips, so two types with the same name in different namespaces would
        /// appear as two identical rows with no way to tell which is which. Only then is the extra
        /// text worth the noise.
        /// </remarks>
        private static string MenuLabel(Type type, List<Type> options)
        {
            var duplicates = 0;

            foreach (var other in options)
            {
                if (string.Equals(other.Name, type.Name, StringComparison.Ordinal))
                {
                    duplicates++;
                }
            }

            return duplicates > 1 ? $"{type.Name}  ({type.Namespace})" : type.Name;
        }
    }
}
