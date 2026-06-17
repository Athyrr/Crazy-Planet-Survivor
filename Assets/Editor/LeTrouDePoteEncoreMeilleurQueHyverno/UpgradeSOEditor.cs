using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(UpgradeSO), true)]
public class UpgradeSOEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        bool isStatUpgrade = target is StatUpgradeSO;

        // Draw properties one by one to inject the help box
        SerializedProperty prop = serializedObject.GetIterator();
        bool enterChildren = true;
        while (prop.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (prop.name == "m_Script") continue;

            if (isStatUpgrade)
            {
                // Stat upgrades are always PlayerStat and now use the Modifiers list:
                // the type + base single-value fields are redundant, hide them.
                if (prop.name == "UpgradeType") continue;
                if (prop.name == "ModifierStrategy") continue;
                if (prop.name == "Value") continue;
            }

            EditorGUILayout.PropertyField(prop, true);

            // Help box is only relevant for the single-value (spell) upgrades.
            if (!isStatUpgrade && prop.name == "Value")
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
            case EModiferStrategy.Flat:
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
            case EModiferStrategy.Multiply:
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

/// <summary>
/// One-time migration from the legacy single-stat <see cref="StatUpgradeSO"/> format
/// (CharacterStat + base Value/ModifierStrategy) to the multi-modifier <c>Modifiers[]</c> list.
/// </summary>
public static class StatUpgradeMigration
{
    [MenuItem("Survivor/Upgrades/Migrate Stat Upgrades to Modifiers")]
    public static void MigrateAll()
    {
        string[] guids = AssetDatabase.FindAssets("t:StatUpgradeSO");
        int migrated = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var so = AssetDatabase.LoadAssetAtPath<StatUpgradeSO>(path);
            if (so == null)
                continue;

            // Only migrate assets that still hold legacy data and have no modifiers yet.
            bool hasModifiers = so.Modifiers != null && so.Modifiers.Length > 0;
            bool hasLegacy = so.LegacyCharacterStat != ECharacterStat.None;
            if (hasModifiers || !hasLegacy)
                continue;

            Undo.RecordObject(so, "Migrate Stat Upgrade");
            so.Modifiers = new[]
            {
                new StatModifier
                {
                    CharacterStat = so.LegacyCharacterStat,
                    Strategy = so.ModifierStrategy,
                    Value = so.Value,
                }
            };
            so.ClearLegacyStat();
            so.Value = 0f;

            EditorUtility.SetDirty(so);
            migrated++;
        }

        if (migrated > 0)
            AssetDatabase.SaveAssets();

        Debug.Log($"[StatUpgradeMigration] Migrated {migrated} stat upgrade(s) to the Modifiers format " +
                  $"out of {guids.Length} found.");
    }
}
