using System.Collections.Generic;

/// <summary>Emitter rules shared by the bake (SpellsDatabaseAuthoring: LogError + entry skipped) and the
/// inspector (SpellSOEditor: HelpBox). Spec "Erreurs et garde-fous", decision Q16.</summary>
public static class SpellEmitterValidation
{
    public const int MaxEmitters = 2;

    /// <summary>Returns, per entry of so.Emitters, whether it is valid; appends one message per error.</summary>
    public static bool[] Validate(SpellSO so, List<string> errors)
    {
        var entries = so.Emitters ?? new SpellEmitterEntry[0];
        var valid = new bool[entries.Length];
        if (entries.Length > MaxEmitters)
            errors.Add($"{so.name}: {entries.Length} emitters declared, max {MaxEmitters} — entries after index {MaxEmitters - 1} are ignored.");

        bool ownerIsArea = so.SpellPrefab != null && so.SpellPrefab.GetComponent<AreaAttackAuthoring>() != null;
        for (int i = 0; i < entries.Length && i < MaxEmitters; i++)
        {
            string error = ValidateEntry(so, entries[i], ownerIsArea);
            if (error != null) errors.Add($"{so.name} emitter[{i}]: {error}");
            else valid[i] = true;
        }
        return valid;
    }

    private static string ValidateEntry(SpellSO so, SpellEmitterEntry e, bool ownerIsArea)
    {
        if (e == null) return "entry is null.";
        if (e.Child == null) return "Child is null.";
        if (e.Capability != ESpellCapability.None && e.Capability != ESpellCapability.Explode && e.Capability != ESpellCapability.Split)
            return "Capability must be None, Explode or Split.";
        if ((e.Count == EEmitterCount.SplitCount) != (e.Capability == ESpellCapability.Split))
            return "Count = SplitCount requires Capability = Split, and a Split entry requires Count = SplitCount.";
        if (e.Capability == ESpellCapability.Explode
            && (e.Child.SpellPrefab == null || e.Child.SpellPrefab.GetComponent<AreaAttackAuthoring>() == null))
            return "an Explode child must be an AreaAttack Burst (AreaAttackAuthoring on its prefab).";
        if (e.Attachment == EEmitterAttachment.Attached && e.Trigger == EEmitterTrigger.OnImpact)
            return "Attached + OnImpact is not delivered (lot 1).";
        if (e.Trigger == EEmitterTrigger.OnImpact && ownerIsArea)
            return "OnImpact on an Area spell is not delivered (lot 1).";
        if (e.Child == so && e.Capability != ESpellCapability.Split)
            return "self-reference is only allowed for Split (bounded by depth).";
        if (e.Child != so && ReachesRoot(so, e.Child, new HashSet<SpellSO>()))
            return $"child cycle through '{e.Child.name}' (only self-Split is allowed).";
        return null;
    }

    private static bool ReachesRoot(SpellSO root, SpellSO node, HashSet<SpellSO> visited)
    {
        if (node == root) return true;
        if (node == null || !visited.Add(node) || node.Emitters == null) return false;
        foreach (var c in node.Emitters)
        {
            if (c == null || c.Child == null || c.Child == node) continue; // self-Split of a child is bounded by depth
            if (ReachesRoot(root, c.Child, visited)) return true;
        }
        return false;
    }
}
