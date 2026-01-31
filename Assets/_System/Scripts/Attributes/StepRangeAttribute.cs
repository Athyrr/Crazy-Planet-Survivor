using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// A custom range attribute that snaps to a specific step increment in the Inspector.
/// </summary>
public class StepRangeAttribute : PropertyAttribute
{
    public readonly float min;
    public readonly float max;
    public readonly float step;

    public StepRangeAttribute(float min, float max, float step)
    {
        this.min = min;
        this.max = max;
        this.step = step;
    }
}

#if UNITY_EDITOR
[CustomPropertyDrawer(typeof(StepRangeAttribute))]
public class StepRangeDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var attr = (StepRangeAttribute)attribute;

        if (property.propertyType == SerializedPropertyType.Float)
        {
            EditorGUI.BeginChangeCheck();
            float newValue = EditorGUI.Slider(position, label, property.floatValue, attr.min, attr.max);
            if (EditorGUI.EndChangeCheck())
            {
                property.floatValue = Mathf.Round(newValue / attr.step) * attr.step;
            }
        }
        else
        {
            EditorGUI.LabelField(position, label.text, "Use StepRange with float fields.");
        }
    }
}
#endif