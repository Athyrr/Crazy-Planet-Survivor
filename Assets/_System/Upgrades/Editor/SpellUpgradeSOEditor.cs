using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SpellUpgradeSO))]
public class SpellUpgradeSOEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        var upgrade = (SpellUpgradeSO)target;
        if (upgrade.SpellID == ESpellID.None)
            return;

        if ((upgrade.RequiredTags & ESpellTag.Explosive) != 0)
            EditorGUILayout.HelpBox("Explosive is derived from the Explode capability: use GrantCapabilities = Explode, not RequiredTags = Explosive.", MessageType.Warning);

        if ((upgrade.GrantCapabilities & ESpellCapability.Explode) == 0)
            return;

        foreach (string guid in AssetDatabase.FindAssets("t:SpellSO"))
        {
            var spell = AssetDatabase.LoadAssetAtPath<SpellSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (spell == null || spell.ID != upgrade.SpellID) continue;
            bool hasExplodeEntry = false;
            if (spell.Emitters != null)
                foreach (var e in spell.Emitters)
                    if (e != null && e.Capability == ESpellCapability.Explode) { hasExplodeEntry = true; break; }
            if (!hasExplodeEntry)
                EditorGUILayout.HelpBox($"This upgrade grants Explode to '{spell.name}' but it has no Explode emitter entry: the upgrade would have no effect.", MessageType.Warning);
            return;
        }
    }
}
