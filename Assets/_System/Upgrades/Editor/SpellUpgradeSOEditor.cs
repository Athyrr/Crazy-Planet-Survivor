using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SpellUpgradeSO))]
public class SpellUpgradeSOEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var upgrade = (SpellUpgradeSO)target;
        if (upgrade.SpellID == ESpellID.None || (upgrade.RequiredTags & ESpellTag.Explosive) == 0)
            return;

        foreach (string guid in AssetDatabase.FindAssets("t:SpellSO"))
        {
            var spell = AssetDatabase.LoadAssetAtPath<SpellSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (spell != null && spell.ID == upgrade.SpellID && !spell.AllowExplode)
            {
                EditorGUILayout.HelpBox(
                    $"This upgrade grants Explosive to '{spell.name}' but its AllowExplode is unchecked: the upgrade would have no effect.",
                    MessageType.Warning);
                return;
            }
        }
    }
}
