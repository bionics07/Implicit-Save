using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ImplicitSave.Editor
{
    /// <summary>
    /// Draws a <see cref="SerializableDictionary{TKey,TValue}"/> as a key/value table instead of the
    /// two parallel lists Unity keeps underneath.
    /// </summary>
    /// <remarks>
    /// Unity's inspector has no idea what a dictionary is; left alone it shows <c>_keys</c> and
    /// <c>_values</c> side by side, and keeping them lined up by hand is how you corrupt a save.
    /// Drawing the pairs together is the whole reason this drawer exists, and it is one of the four
    /// things this package is sold on.
    /// <para>
    /// One drawer covers every <c>&lt;K,V&gt;</c> combination, which template-based property drawers
    /// have allowed since Unity 2020.1. Before that it would have taken a concrete subclass per
    /// combination - a tax on the user this package is not willing to charge.
    /// </para>
    /// </remarks>
    [CustomPropertyDrawer(typeof(SerializableDictionary<,>), true)]
    public class SerializableDictionaryDrawer : PropertyDrawer
    {
        private const float RowSpacing = 2f;
        private const float RemoveButtonWidth = 22f;
        private const float KeyRatio = 0.42f;

        /// <inheritdoc />
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var line = EditorGUIUtility.singleLineHeight;

            if (!property.isExpanded)
            {
                return line;
            }

            var keys = property.FindPropertyRelative("_keys");
            var values = property.FindPropertyRelative("_values");

            if (keys == null || values == null)
            {
                return line * 2;
            }

            // header + rows + add button, plus a line for the duplicate warning when there is one.
            var height = line + RowSpacing;

            for (var i = 0; i < keys.arraySize; i++)
            {
                height += RowHeight(keys, values, i) + RowSpacing;
            }

            height += line + RowSpacing;

            if (HasDuplicateKeys(keys, out _))
            {
                height += line * 2 + RowSpacing;
            }

            return height;
        }

        /// <inheritdoc />
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            var keys = property.FindPropertyRelative("_keys");
            var values = property.FindPropertyRelative("_values");

            var line = EditorGUIUtility.singleLineHeight;
            var headerRect = new Rect(position.x, position.y, position.width, line);

            if (keys == null || values == null)
            {
                EditorGUI.LabelField(headerRect, label, new GUIContent("(not a SerializableDictionary)"));
                return;
            }

            property.isExpanded = EditorGUI.Foldout(
                headerRect, property.isExpanded, $"{label.text}  ({keys.arraySize})", toggleOnLabelClick: true);

            if (!property.isExpanded)
            {
                return;
            }

            var duplicated = HasDuplicateKeys(keys, out var duplicateIndices);
            var y = position.y + line + RowSpacing;

            using (new EditorGUI.IndentLevelScope())
            {
                for (var i = 0; i < keys.arraySize; i++)
                {
                    var rowHeight = RowHeight(keys, values, i);
                    var rowRect = new Rect(position.x, y, position.width, rowHeight);

                    if (DrawRow(rowRect, keys, values, i, duplicateIndices.Contains(i)))
                    {
                        // Removing shifts everything after it, so stop drawing this frame.
                        return;
                    }

                    y += rowHeight + RowSpacing;
                }

                var addRect = new Rect(position.x, y, position.width, line);
                DrawAddButton(addRect, keys, values);
                y += line + RowSpacing;

                if (duplicated)
                {
                    var warningRect = new Rect(position.x, y, position.width, line * 2);
                    EditorGUI.HelpBox(
                        warningRect,
                        "Duplicate keys, highlighted below. Only the first of each is kept when this loads - " +
                        "the others are dropped.",
                        MessageType.Error);
                }
            }
        }

        /// <summary>Draws one key/value pair. Returns true when the row was removed.</summary>
        private static bool DrawRow(Rect rect, SerializedProperty keys, SerializedProperty values, int index,
            bool isDuplicate)
        {
            var key = keys.GetArrayElementAtIndex(index);
            var value = values.arraySize > index ? values.GetArrayElementAtIndex(index) : null;

            var keyWidth = (rect.width - RemoveButtonWidth) * KeyRatio;
            var valueWidth = rect.width - RemoveButtonWidth - keyWidth - 4f;

            var keyRect = new Rect(rect.x, rect.y, keyWidth, EditorGUI.GetPropertyHeight(key, GUIContent.none, true));
            var valueRect = new Rect(rect.x + keyWidth + 4f, rect.y, valueWidth,
                value != null ? EditorGUI.GetPropertyHeight(value, GUIContent.none, true) : rect.height);
            var removeRect = new Rect(rect.xMax - RemoveButtonWidth, rect.y, RemoveButtonWidth,
                EditorGUIUtility.singleLineHeight);

            if (isDuplicate && Event.current.type == EventType.Repaint)
            {
                // The whole row, not just the field's outline: this is the thing to fix before
                // saving, and a thin border is easy to miss in a long table.
                SaveEditorStyles.EnsureBuilt();
                var band = new Rect(rect.x - 2f, rect.y - 1f, rect.width + 4f, rect.height + 2f);
                EditorGUI.DrawRect(band, new Color(
                    SaveEditorStyles.Danger.r, SaveEditorStyles.Danger.g, SaveEditorStyles.Danger.b, 0.10f));
            }

            var previousColor = GUI.color;
            if (isDuplicate)
            {
                GUI.color = new Color(1f, 0.62f, 0.6f);
            }

            EditorGUI.PropertyField(keyRect, key, GUIContent.none, true);
            GUI.color = previousColor;

            if (value != null)
            {
                EditorGUI.PropertyField(valueRect, value, GUIContent.none, true);
            }

            if (!GUI.Button(removeRect, new GUIContent("×", "Remove this entry")))
            {
                return false;
            }

            keys.DeleteArrayElementAtIndex(index);

            if (values.arraySize > index)
            {
                values.DeleteArrayElementAtIndex(index);
            }

            return true;
        }

        private static void DrawAddButton(Rect rect, SerializedProperty keys, SerializedProperty values)
        {
            var buttonRect = new Rect(rect.x, rect.y, 110f, rect.height);

            if (!GUI.Button(buttonRect, "+ Add entry"))
            {
                return;
            }

            var index = keys.arraySize;
            keys.InsertArrayElementAtIndex(index);
            values.InsertArrayElementAtIndex(index);

            ResetToDefault(values.GetArrayElementAtIndex(index));
            AssignFreeKey(keys, index);
        }

        /// <summary>
        /// Gives a new row a key nothing else is using.
        /// </summary>
        /// <remarks>
        /// Unity copies the previous element when it grows an array, so a new row would start as an
        /// exact duplicate. Even blanking it is not enough: with an enum key the first value is
        /// usually already taken, and adding a second entry would collide before anything could be
        /// typed. Picking a free value means "add" always produces a usable row.
        /// </remarks>
        private static void AssignFreeKey(SerializedProperty keys, int index)
        {
            var element = keys.GetArrayElementAtIndex(index);

            switch (element.propertyType)
            {
                case SerializedPropertyType.Enum:
                {
                    // The first enum value not already in the table, or value 0 when they are all
                    // taken - at that point a duplicate is unavoidable and gets flagged in red.
                    var used = UsedValues(keys, index, p => p.enumValueIndex);

                    for (var i = 0; i < element.enumNames.Length; i++)
                    {
                        if (used.Contains(i))
                        {
                            continue;
                        }

                        element.enumValueIndex = i;
                        return;
                    }

                    element.enumValueIndex = 0;
                    return;
                }

                case SerializedPropertyType.Integer:
                {
                    var used = UsedValues(keys, index, p => (int)p.longValue);
                    var candidate = 0;

                    while (used.Contains(candidate))
                    {
                        candidate++;
                    }

                    element.longValue = candidate;
                    return;
                }

                case SerializedPropertyType.String:
                {
                    var used = new HashSet<string>(StringComparer.Ordinal);

                    for (var i = 0; i < keys.arraySize; i++)
                    {
                        if (i != index)
                        {
                            used.Add(keys.GetArrayElementAtIndex(i).stringValue ?? string.Empty);
                        }
                    }

                    // An empty key is the friendliest starting point, but only while it is free.
                    if (!used.Contains(string.Empty))
                    {
                        element.stringValue = string.Empty;
                        return;
                    }

                    var suffix = 1;
                    while (used.Contains("key" + suffix))
                    {
                        suffix++;
                    }

                    element.stringValue = "key" + suffix;
                    return;
                }

                default:
                    ResetToDefault(element);
                    return;
            }
        }

        private static HashSet<int> UsedValues(SerializedProperty keys, int skipIndex, Func<SerializedProperty, int> read)
        {
            var used = new HashSet<int>();

            for (var i = 0; i < keys.arraySize; i++)
            {
                if (i != skipIndex)
                {
                    used.Add(read(keys.GetArrayElementAtIndex(i)));
                }
            }

            return used;
        }

        /// <summary>Clears a freshly inserted element to its type's zero value.</summary>
        private static void ResetToDefault(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.String:
                    property.stringValue = string.Empty;
                    break;
                case SerializedPropertyType.Integer:
                    property.intValue = 0;
                    break;
                case SerializedPropertyType.Float:
                    property.floatValue = 0f;
                    break;
                case SerializedPropertyType.Boolean:
                    property.boolValue = false;
                    break;
                case SerializedPropertyType.Enum:
                    property.enumValueIndex = 0;
                    break;
                case SerializedPropertyType.ObjectReference:
                    property.objectReferenceValue = null;
                    break;
            }
        }

        /// <summary>
        /// Finds repeated keys. The backing list allows what a dictionary cannot, so the drawer has
        /// to point at the problem - silently losing an entry on the next load would be worse.
        /// </summary>
        internal static bool HasDuplicateKeys(SerializedProperty keys, out HashSet<int> duplicateIndices)
        {
            duplicateIndices = new HashSet<int>();
            var seen = new Dictionary<string, int>(StringComparer.Ordinal);

            for (var i = 0; i < keys.arraySize; i++)
            {
                var text = Describe(keys.GetArrayElementAtIndex(i));

                if (seen.TryGetValue(text, out var first))
                {
                    duplicateIndices.Add(first);
                    duplicateIndices.Add(i);
                    continue;
                }

                seen[text] = i;
            }

            return duplicateIndices.Count > 0;
        }

        private static string Describe(SerializedProperty property)
        {
            switch (property.propertyType)
            {
                case SerializedPropertyType.String:
                    return property.stringValue ?? string.Empty;
                case SerializedPropertyType.Integer:
                    return property.longValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case SerializedPropertyType.Enum:
                    return property.enumValueIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);
                case SerializedPropertyType.Boolean:
                    return property.boolValue ? "true" : "false";
                case SerializedPropertyType.Float:
                    return property.doubleValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
                default:
                    return property.propertyPath;
            }
        }

        private static float RowHeight(SerializedProperty keys, SerializedProperty values, int index)
        {
            var keyHeight = EditorGUI.GetPropertyHeight(keys.GetArrayElementAtIndex(index), GUIContent.none, true);
            var valueHeight = values.arraySize > index
                ? EditorGUI.GetPropertyHeight(values.GetArrayElementAtIndex(index), GUIContent.none, true)
                : EditorGUIUtility.singleLineHeight;

            return Mathf.Max(keyHeight, valueHeight);
        }
    }
}
