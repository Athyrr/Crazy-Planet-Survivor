using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(UpgradeSO), true)]
public class UpgradeSOEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        bool isStatUpgrade = target is CharacterStatUpgradeSO;

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
        ESpellStat stat = target is SpellUpgradeSO spellUpgrade ? spellUpgrade.SpellStat : ESpellStat.None;
        bool isAbsoluteStat = IsAbsoluteSpellStat(stat);
        MessageType messageType = MessageType.Info;

        // All modifiers are additive (stat += Value).
        string helpText = "Additive: Value 0.1 = +10% on a multiplier stat (Damage, Size, Speed...), " +
                          "Value 5 = +5 on an absolute stat (Amount, Bounce, Pierce, Split...).";

        if (!isAbsoluteStat && upgrade.Value >= 2.0f)
        {
            helpText += $"\n\nWARNING: Value {upgrade.Value} on a multiplier stat means +{upgrade.Value * 100f:0.##}%. " +
                        $"If you meant +{upgrade.Value}%, use Value {upgrade.Value / 100f}.";
            messageType = MessageType.Warning;
        }

        if (!string.IsNullOrEmpty(helpText))
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.HelpBox(helpText, messageType);
            EditorGUILayout.Space(5);
        }
    }

    /// <summary>Integer stats: Value is a whole-number count, not a multiplier delta.</summary>
    private static bool IsAbsoluteSpellStat(ESpellStat stat)
        => stat == ESpellStat.Amount || stat == ESpellStat.BounceCount || stat == ESpellStat.PierceCount ||
           stat == ESpellStat.SplitCount || stat == ESpellStat.SplitDepth;
}

/// <summary>
/// One-time migration from the legacy single-stat <see cref="CharacterStatUpgradeSO"/> format
/// (CharacterStat + base Value) to the multi-modifier <c>Modifiers[]</c> list.
/// </summary>
public static class StatUpgradeMigration
{
    [MenuItem("Survivor/Upgrades/Migrate Stat Upgrades to Modifiers")]
    public static void MigrateAll()
    {
        string[] guids = AssetDatabase.FindAssets("t:CharacterStatUpgradeSO");
        int migrated = 0;

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var so = AssetDatabase.LoadAssetAtPath<CharacterStatUpgradeSO>(path);
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
