using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(SpellDataSO))]
public class SpellDataSOEditor : UnityEditor.Editor
{
    public override void OnInspectorGUI()
    {
        var so = (SpellDataSO)target;

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
            Undo.RecordObject(so, "Derive SpellDataSO.Tags from Effects[]");
            so.Tags = newTags;
            EditorUtility.SetDirty(so);
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Derived Tags (read-only)", newTags.ToString());
    }
}
