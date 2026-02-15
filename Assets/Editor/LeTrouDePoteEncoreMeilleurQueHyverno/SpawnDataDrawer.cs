using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom property drawer for SpawnGroupData.
/// It dynamically adjusts the inspector UI based on the selected SpawnMode,
/// showing only relevant fields for a cleaner experience.
/// </summary>
[CustomPropertyDrawer(typeof(EnemiesSpawnerAuthoring.SpawnGroupData))]
public class SpawnGroupDataDrawer : PropertyDrawer
{
    // Constants for layout
    private const float VerticalSpacing = 2f;

    /// <summary>
    /// Calculates the total height of the property in the inspector based on visible fields.
    /// </summary>
    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        var modeProp = property.FindPropertyRelative("Mode");

        // Retrieve the actual Enum value safely using the index
        SpawnMode currentMode = (SpawnMode)modeProp.enumValueIndex;

        // Base height: Mode + Prefab + Amount + Delay (4 lines)
        int lines = 4;

        // Add lines based on the selected mode
        if (currentMode == SpawnMode.Zone)
        {
            lines += 1; // ZoneTransform
        }
        else if (currentMode == SpawnMode.AroundPlayer)
        {
            lines += 2; // MinRange + MaxRange
        }
        // "RandomInPlanet" and "PlayerOpposite" use default lines

        // Calculate final height with standard spacing
        return lines * EditorGUIUtility.singleLineHeight + (lines - 1) * VerticalSpacing;
    }

    /// <summary>
    /// Renders the GUI for the property.
    /// </summary>
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        EditorGUI.BeginProperty(position, label, property);

        // Retrieve properties
        var modeProp = property.FindPropertyRelative("Mode");
        var prefabProp = property.FindPropertyRelative("Prefab");
        var amountProp = property.FindPropertyRelative("Amount");
        var zoneTransformProp = property.FindPropertyRelative("ZoneTransform");
        var delayProp = property.FindPropertyRelative("Delay");
        var minRangeProp = property.FindPropertyRelative("MinRange");
        var maxRangeProp = property.FindPropertyRelative("MaxRange");

        // Retrieve the actual Enum value safely
        SpawnMode currentMode = (SpawnMode)modeProp.enumValueIndex;

        // Set up the initial rect for the first line
        Rect rect = new Rect(position.x, position.y, position.width, EditorGUIUtility.singleLineHeight);

        // 1. Draw Mode
        EditorGUI.PropertyField(rect, modeProp);
        rect.y += EditorGUIUtility.singleLineHeight + VerticalSpacing;

        // 2. Draw Prefab (Always visible)
        EditorGUI.PropertyField(rect, prefabProp);
        rect.y += EditorGUIUtility.singleLineHeight + VerticalSpacing;

        // 3. Draw Amount (Always visible)
        EditorGUI.PropertyField(rect, amountProp);
        rect.y += EditorGUIUtility.singleLineHeight + VerticalSpacing;

        // 4. Draw Delay (Always visible)
        EditorGUI.PropertyField(rect, delayProp);
        rect.y += EditorGUIUtility.singleLineHeight + VerticalSpacing;

        // 5. Draw Conditional Fields based on Mode
        if (currentMode == SpawnMode.Zone)
        {
            EditorGUI.PropertyField(rect, zoneTransformProp);
            rect.y += EditorGUIUtility.singleLineHeight + VerticalSpacing;
        }
        else if (currentMode == SpawnMode.AroundPlayer)
        {
            // Draw Min Range
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(rect, minRangeProp);
            if (EditorGUI.EndChangeCheck())
            {
                // Auto-correct: Min cannot be greater than Max
                if (minRangeProp.floatValue > maxRangeProp.floatValue)
                {
                    maxRangeProp.floatValue = minRangeProp.floatValue;
                }
            }
            rect.y += EditorGUIUtility.singleLineHeight + VerticalSpacing;

            // Draw Max Range
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(rect, maxRangeProp);
            if (EditorGUI.EndChangeCheck())
            {
                // Auto-correct: Max cannot be smaller than Min
                if (maxRangeProp.floatValue < minRangeProp.floatValue)
                {
                    minRangeProp.floatValue = maxRangeProp.floatValue;
                }
            }
            rect.y += EditorGUIUtility.singleLineHeight + VerticalSpacing;
        }

        EditorGUI.EndProperty();
    }
}