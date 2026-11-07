using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SpellSO))]
public class SpellSOEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        var so = (SpellSO)target;

        serializedObject.Update();
        DrawDefaultInspector();

        // Derive Tags' status-effect bits from Effects[] and the Bouncing/Piercing bits from their
        // corresponding count fields — remaining behavior/form bits (Ranged/Melee/Projectile/Area/
        // Summon/Buff/Debuff/Explosive) are kept as whatever the field already carries (this task
        // does not touch how those are authored), only the bits listed above are recomputed.
        ESpellTag behaviorBits = so.Tags & ~(ESpellTag.Burn | ESpellTag.Slow | ESpellTag.Stun | ESpellTag.Knockback
            | ESpellTag.Piercing | ESpellTag.Bouncing);
        ESpellTag derivedStatusBits = ESpellTag.None;
        foreach (var effect in so.Effects)
        {
            derivedStatusBits |= effect.Type switch
            {
                EffectType.Burn => ESpellTag.Burn,
                EffectType.Slow => ESpellTag.Slow,
                EffectType.Stun => ESpellTag.Stun,
                EffectType.Knockback => ESpellTag.Knockback,
                _ => ESpellTag.None,
            };
        }

        if (so.Pierces > 0) derivedStatusBits |= ESpellTag.Piercing;
        if (so.Bounces > 0) derivedStatusBits |= ESpellTag.Bouncing;

        ESpellTag newTags = behaviorBits | derivedStatusBits;
        if (newTags != so.Tags)
        {
            Undo.RecordObject(so, "Derive SpellSO.Tags from Effects[]");
            so.Tags = newTags;
            EditorUtility.SetDirty(so);
        }

        // Capability warnings: the SO says "allowed", the prefab must carry the slot; counters need the permission.
        if (so.AllowBounce && so.SpellPrefab != null && so.SpellPrefab.GetComponent<BounceAuthoring>() == null)
            EditorGUILayout.HelpBox("AllowBounce is checked but the prefab has no BounceAuthoring slot.", MessageType.Warning);
        if (so.AllowPierce && so.SpellPrefab != null && so.SpellPrefab.GetComponent<PierceAuthoring>() == null)
            EditorGUILayout.HelpBox("AllowPierce is checked but the prefab has no PierceAuthoring slot.", MessageType.Warning);
        if (so.Bounces > 0 && !so.AllowBounce)
            EditorGUILayout.HelpBox("Bounces > 0 but AllowBounce is unchecked: the bounces will never fire.", MessageType.Warning);
        if (so.Pierces > 0 && !so.AllowPierce)
            EditorGUILayout.HelpBox("Pierces > 0 but AllowPierce is unchecked: the pierces will never fire.", MessageType.Warning);

        // Emitter validation (same rules as the bake, which logs and skips invalid entries).
        var emitterErrors = new System.Collections.Generic.List<string>();
        SpellEmitterValidation.Validate(so, emitterErrors);
        foreach (var err in emitterErrors)
            EditorGUILayout.HelpBox(err, MessageType.Error);

        // Warning: this spell is used as an emitter child AND is the target of an UnlockSpell upgrade.
        bool usedAsChild = false;
        foreach (string guid in AssetDatabase.FindAssets("t:SpellSO"))
        {
            var other = AssetDatabase.LoadAssetAtPath<SpellSO>(AssetDatabase.GUIDToAssetPath(guid));
            if (other == null || other == so || other.Emitters == null) continue;
            foreach (var e in other.Emitters)
                if (e != null && e.Child == so) { usedAsChild = true; break; }
            if (usedAsChild) break;
        }
        if (usedAsChild)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:SpellUpgradeSO"))
            {
                var up = AssetDatabase.LoadAssetAtPath<SpellUpgradeSO>(AssetDatabase.GUIDToAssetPath(guid));
                if (up != null && up.UpgradeType == EUpgradeType.UnlockSpell && up.SpellID == so.ID)
                {
                    EditorGUILayout.HelpBox($"'{so.name}' is an emitter child AND is unlocked by '{up.name}': it would also be cast on its own.", MessageType.Warning);
                    break;
                }
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Derived Tags (read-only)", newTags.ToString());
    }
}
