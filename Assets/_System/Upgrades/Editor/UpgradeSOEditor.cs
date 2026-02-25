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
        
        switch (upgrade.ModifierStrategy)
        {
            case EStatModiferStrategy.Flat:
                helpText = "Flat: Stat = Stat + Value\n" +
                           "Example: Value 5 means +5 to the base stat.";
                break;
            case EStatModiferStrategy.Multiply:
                helpText = "Multiply: Stat = Stat * Value\n" +
                           "Example: Value 1.1 = +10%, Value 0.9 = -10%.\n" +
                           "Base value is 1.0 (100%).";
                break;
        }

        if (!string.IsNullOrEmpty(helpText))
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox(helpText, MessageType.Info);
            EditorGUILayout.Space(5);
        }
    }
}
