using UnityEditor;
using UnityEngine;

/// <summary>Draws Overridable&lt;T&gt; as a checkbox + value on one line — the value field is disabled
/// (but still visible, so a designer can see what it WOULD be) when Override is unchecked.</summary>
[CustomPropertyDrawer(typeof(Overridable<>))]
public class OverridableDrawer : PropertyDrawer
{
    private const float CheckboxWidth = 20f;

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        SerializedProperty overrideProp = property.FindPropertyRelative("Override");
        SerializedProperty valueProp = property.FindPropertyRelative("Value");

        EditorGUI.BeginProperty(position, label, property);

        var checkboxRect = new Rect(position.x, position.y, CheckboxWidth, position.height);
        var labelRect = new Rect(position.x + CheckboxWidth, position.y,
            EditorGUIUtility.labelWidth - CheckboxWidth, position.height);
        var valueRect = new Rect(position.x + EditorGUIUtility.labelWidth, position.y,
            position.width - EditorGUIUtility.labelWidth, position.height);

        EditorGUI.PropertyField(checkboxRect, overrideProp, GUIContent.none);
        EditorGUI.LabelField(labelRect, label);

        using (new EditorGUI.DisabledScope(!overrideProp.boolValue))
        {
            EditorGUI.PropertyField(valueRect, valueProp, GUIContent.none);
        }

        EditorGUI.EndProperty();
    }
}
