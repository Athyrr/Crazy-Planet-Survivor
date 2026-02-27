using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(UpgradeSO))]
public class UpgradeSOEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        // Draw properties one by one to inject the help box
        SerializedProperty prop = serializedObject.GetIterator();
        bool enterChildren = true;
        while (prop.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (prop.name == "m_Script") continue;

            EditorGUILayout.PropertyField(prop, true);

            if (prop.name == "Value")
            {
                ShowHelpBox();
            }
        }

        serializedObject.ApplyModifiedProperties();
    }

    private void ShowHelpBox()
    {
        UpgradeSO upgrade = (UpgradeSO)target;
        string helpText = "";
        MessageType messageType = MessageType.Info;
        
        switch (upgrade.ModifierStrategy)
        {
            case EStatModiferStrategy.Flat:
                helpText = "Flat: Stat = Stat + Value\n" +
                           "Example: Value 5 means +5 to the base stat.";

                // Verification: Check if value looks like a converted percentage (e.g. 1.1 or 0.9)
                if (upgrade.Value >= 0.001f && upgrade.Value <= 1.999f && (upgrade.Value % 1) != 0)
                {
                    float potentialPerc = (upgrade.Value - 1f) * 100f;
                    helpText += $"\n\nWARNING: Value {upgrade.Value} looks like a percentage multiplier ({potentialPerc:+0.##;-0.##}%). " +
                                "If you intended a percentage, switch to 'Multiply'.";
                    messageType = MessageType.Warning;
                }
                break;
            case EStatModiferStrategy.Multiply:
                float percentage = (upgrade.Value - 1f) * 100f;
                helpText = "Multiply: Stat = Stat * Value\n" +
                           "Example: Value 1.1 = +10%, Value 0.9 = -10%.\n" +
                           $"Base value is 1.0 (100%).\n\n" +
                           $"Current Representation: {percentage:+0.##;-0.##}%";

                if (upgrade.Value >= 2.0f)
                {
                    helpText += $"\n\nWARNING: Value {upgrade.Value} represents +{percentage}%. " +
                                $"If you meant +{upgrade.Value}%, use Value {1f + (upgrade.Value / 100f)}.";
                    messageType = MessageType.Warning;
                }
                break;
        }

        if (!string.IsNullOrEmpty(helpText))
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox(helpText, messageType);
            EditorGUILayout.Space(5);
        }
    }
}
