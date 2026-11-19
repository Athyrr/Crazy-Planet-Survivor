using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// SpellSO inspector. Family is drawn first and decides which fields are shown: a field specific to other families is
/// hidden, never cleared (its value stays serialized and baked). Then family/prefab warnings, derived Tags and the
/// validation HelpBoxes.
/// </summary>
[CustomEditor(typeof(SpellSO))]
public class SpellSOEditor : UnityEditor.Editor
{
    // Read only for a Projectile.
    private static readonly HashSet<string> ProjectileFields = new HashSet<string>
    {
        nameof(SpellSO.Bounces), nameof(SpellSO.BounceRange), nameof(SpellSO.Pierces),
        nameof(SpellSO.AllowBounce), nameof(SpellSO.AllowPierce),
    };

    // Baked but never read for a Summon spell: it has no SpellPrefab, its agents cast.
    private static readonly HashSet<string> UnreadBySummon = new HashSet<string>
    {
        nameof(SpellSO.BaseDamage), nameof(SpellSO.BaseSize), nameof(SpellSO.BaseSpeed), nameof(SpellSO.BaseSpawnOffset),
    };

    // Fan fields: read whenever Amount becomes several instances (Spread, MultiTarget surplus, enemy MultiTarget).
    private static readonly HashSet<string> SpreadFields = new HashSet<string>
    {
        nameof(SpellSO.SpreadAngleDegrees), nameof(SpellSO.MaxSpreadDegrees),
    };

    /// <summary>
    /// Whether the inspector shows this top-level field. A hidden field that a warning asks to clear (SpellPrefab of a
    /// Summon, Summon outside the Summon family) stays shown while it is set, so it can be fixed.
    /// </summary>
    public static bool IsFieldVisible(SpellSO spell, string fieldName)
    {
        ESpellFamily family = spell.Family;
        if (ProjectileFields.Contains(fieldName))
            return family == ESpellFamily.Projectile;
        if (SpreadFields.Contains(fieldName))
            return spell.MultiCast != ESpellMultiCast.Single;
        if (fieldName == nameof(SpellSO.TickRate))
            return family == ESpellFamily.Area || family == ESpellFamily.EmitterOwner;
        if (fieldName == nameof(SpellSO.Summon))
            return family == ESpellFamily.Summon || spell.Summon != null;
        if (fieldName == nameof(SpellSO.SpellPrefab))
            return family != ESpellFamily.Summon || spell.SpellPrefab != null;
        if (UnreadBySummon.Contains(fieldName))
            return family != ESpellFamily.Summon;
        return true;
    }

    /// <summary>
    /// Family vs prefab consistency, minimal rules. Prefab rules read the prefab root and only run when a prefab is set.
    /// </summary>
    public static void CollectFamilyWarnings(SpellSO spell, List<string> warnings)
    {
        GameObject prefab = spell.SpellPrefab;
        switch (spell.Family)
        {
            case ESpellFamily.Projectile:
                if (prefab != null && prefab.GetComponent<DamageOnContactAuthoring>() == null)
                    warnings.Add("Projectile family but the prefab has no DamageOnContactAuthoring.");
                break;
            case ESpellFamily.Area:
                if (prefab != null && prefab.GetComponent<DestroyOnContactAuthoring>() != null)
                    warnings.Add("Area family but the prefab has a DestroyOnContactAuthoring: an Area is not consumed on contact.");
                break;
            case ESpellFamily.Summon:
                // With Summon set, the Summon spell rules below already report a SpellPrefab as an error.
                if (spell.Summon == null)
                {
                    warnings.Add("Summon family but the Summon field is empty.");
                    if (prefab != null)
                        warnings.Add("Summon family: SpellPrefab must be empty (the agent prefab comes from the SummonSO).");
                }
                break;
            case ESpellFamily.EmitterOwner:
                if (prefab != null && prefab.GetComponent<EmitterOwnerAuthoring>() == null)
                    warnings.Add("EmitterOwner family but the prefab has no EmitterOwnerAuthoring.");
                break;
        }

        if (spell.Family != ESpellFamily.Summon && spell.Summon != null)
            warnings.Add($"{spell.Family} family but the Summon field is set: only a Summon spell references a SummonSO.");
    }

    public override void OnInspectorGUI()
    {
        var so = (SpellSO)target;

        serializedObject.Update();
        using (new EditorGUI.DisabledScope(true))
            EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
        // Family first: it decides which of the fields below are shown.
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(SpellSO.Family)));

        SerializedProperty it = serializedObject.GetIterator();
        bool enterChildren = true;
        while (it.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (it.propertyPath == "m_Script" || it.propertyPath == nameof(SpellSO.Family))
                continue;
            if (IsFieldVisible(so, it.propertyPath))
                EditorGUILayout.PropertyField(it, true);
        }
        serializedObject.ApplyModifiedProperties();

        var familyWarnings = new List<string>();
        CollectFamilyWarnings(so, familyWarnings);
        foreach (string warning in familyWarnings)
            EditorGUILayout.HelpBox(warning, MessageType.Warning);

        // Derive Tags' status-effect bits from Effects[], the Bouncing/Piercing bits from their
        // corresponding count fields, and Summon from SpellSO.Summon — remaining behavior/form bits
        // (Ranged/Melee/Projectile/Area/Buff/Debuff/Explosive) are kept as whatever the field already
        // carries, only the bits listed above are recomputed.
        ESpellTag behaviorBits = so.Tags & ~(ESpellTag.Burn | ESpellTag.Slow | ESpellTag.Stun | ESpellTag.Knockback
            | ESpellTag.Piercing | ESpellTag.Bouncing | ESpellTag.Summon);
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

        // Summon is derived from the delivery family (SpellSO.Summon), never hand-authored (the bake derives it too).
        if (so.Summon != null) derivedStatusBits |= ESpellTag.Summon;

        ESpellTag newTags = behaviorBits | derivedStatusBits;
        if (newTags != so.Tags)
        {
            Undo.RecordObject(so, "Derive SpellSO.Tags from Effects[]");
            so.Tags = newTags;
            EditorUtility.SetDirty(so);
        }

        // Capability warnings: the SO says "allowed", the prefab must carry the slot; counters need the permission.
        // Silent while these Projectile fields are hidden.
        if (IsFieldVisible(so, nameof(SpellSO.AllowBounce)))
        {
            if (so.AllowBounce && so.SpellPrefab != null && so.SpellPrefab.GetComponent<BounceAuthoring>() == null)
                EditorGUILayout.HelpBox("AllowBounce is checked but the prefab has no BounceAuthoring slot.", MessageType.Warning);
            if (so.AllowPierce && so.SpellPrefab != null && so.SpellPrefab.GetComponent<PierceAuthoring>() == null)
                EditorGUILayout.HelpBox("AllowPierce is checked but the prefab has no PierceAuthoring slot.", MessageType.Warning);
            if (so.Bounces > 0 && !so.AllowBounce)
                EditorGUILayout.HelpBox("Bounces > 0 but AllowBounce is unchecked: the bounces will never fire.", MessageType.Warning);
            if (so.Pierces > 0 && !so.AllowPierce)
                EditorGUILayout.HelpBox("Pierces > 0 but AllowPierce is unchecked: the pierces will never fire.", MessageType.Warning);
        }

        // Summon family rules (spec allegeance-summon §2; the bake logs the same as errors).
        if (so.Summon != null)
        {
            if (so.SpellPrefab != null)
                EditorGUILayout.HelpBox("Summon spell: SpellPrefab must be empty (the agent prefab comes from the SummonSO).", MessageType.Error);
            if (so.BaseCooldown > 0f)
                EditorGUILayout.HelpBox("Summon spell: BaseCooldown must be <= 0 (passive; the agents cast, not this spell).", MessageType.Error);
            if (so.BaseAmount < 1)
                EditorGUILayout.HelpBox("Summon spell: BaseAmount must be >= 1 (Amount = agents alive; FinalAmount = max(1, …) would swallow the first +1).", MessageType.Error);
        }

        // Emitter validation (same rules as the bake, which logs and skips invalid entries).
        var emitterErrors = new List<string>();
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
