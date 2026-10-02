using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

/// <summary>Draws Overridable&lt;T&gt; as a checkbox + value on one line. When Override is checked, the
/// value field is editable. When unchecked, it shows — disabled — the value actually inherited by walking
/// the container's `BaseTemplate` chain (not this object's own unset `Value`), so a designer always sees
/// what the field WOULD resolve to. Falls back to this object's own (likely default/zero) Value when no
/// `BaseTemplate` field exists on the container type, or the chain bottoms out at null.</summary>
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
            SerializedProperty displayProp = overrideProp.boolValue
                ? valueProp
                : ResolveInheritedValue(property) ?? valueProp;
            EditorGUI.PropertyField(valueRect, displayProp, GUIContent.none);
        }

        EditorGUI.EndProperty();
    }

    /// <summary>Walks the container's `BaseTemplate` chain (by convention: a public instance field of that
    /// exact name) looking for the first ancestor whose own Override is true for this same field, and
    /// returns ITS Value property to display. Returns null if the container has no `BaseTemplate` field, the
    /// chain hits a null reference, or a cycle is detected (defensive — BaseTemplate chains are
    /// human-authored data).</summary>
    private static SerializedProperty ResolveInheritedValue(SerializedProperty property)
    {
        var current = property;
        var visited = new HashSet<Object>();

        while (true)
        {
            Object targetObject = current.serializedObject.targetObject;
            FieldInfo baseField = targetObject.GetType()
                .GetField("BaseTemplate", BindingFlags.Public | BindingFlags.Instance);
            if (baseField == null)
                return null;

            var baseAsset = baseField.GetValue(targetObject) as Object;
            if (baseAsset == null || !visited.Add(baseAsset))
                return null;

            var baseSerialized = new SerializedObject(baseAsset);
            SerializedProperty baseProp = baseSerialized.FindProperty(current.propertyPath);
            if (baseProp == null)
                return null;

            SerializedProperty baseOverride = baseProp.FindPropertyRelative("Override");
            if (baseOverride.boolValue)
                return baseProp.FindPropertyRelative("Value");

            current = baseProp;
        }
    }
}
