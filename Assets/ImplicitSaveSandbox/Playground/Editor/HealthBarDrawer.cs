using UnityEditor;
using UnityEngine;

/// <summary>
/// Property drawer do "usuário", não do package. Existe para provar um dos critérios da Fase 7: a
/// janela de save respeita os drawers que você já escreveu, porque desenha pelo Inspector da Unity
/// em vez de desenhar o JSON na mão.
/// </summary>
/// <remarks>
/// Se a janela mostrasse este campo como dois floats soltos em vez da barra colorida, o critério
/// teria falhado.
/// </remarks>
[CustomPropertyDrawer(typeof(HealthBar))]
public class HealthBarDrawer : PropertyDrawer
{
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        return EditorGUIUtility.singleLineHeight * 2 + 4f;
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var fill = property.FindPropertyRelative("Fill");
        var tint = property.FindPropertyRelative("Tint");

        var line = EditorGUIUtility.singleLineHeight;
        var labelRect = new Rect(position.x, position.y, position.width, line);
        EditorGUI.LabelField(labelRect, label.text + "  (drawer customizado)", EditorStyles.miniBoldLabel);

        var barRect = new Rect(position.x, position.y + line + 2f, position.width - 60f, line);
        EditorGUI.DrawRect(barRect, new Color(0.15f, 0.15f, 0.15f));

        var filled = new Rect(barRect.x, barRect.y, barRect.width * Mathf.Clamp01(fill.floatValue), barRect.height);
        EditorGUI.DrawRect(filled, tint.colorValue);

        var valueRect = new Rect(barRect.xMax + 4f, barRect.y, 56f, line);
        fill.floatValue = Mathf.Clamp01(EditorGUI.FloatField(valueRect, fill.floatValue));

        // Clique na barra ajusta o valor, para dar o que mexer.
        if (Event.current.type == EventType.MouseDown && barRect.Contains(Event.current.mousePosition))
        {
            fill.floatValue = Mathf.Clamp01((Event.current.mousePosition.x - barRect.x) / barRect.width);
            Event.current.Use();
        }
    }
}
