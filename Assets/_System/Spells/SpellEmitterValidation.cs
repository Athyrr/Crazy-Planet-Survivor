using System.Collections.Generic;

/// <summary>Emitter rules shared by the bake (SpellsDatabaseAuthoring: LogError + entry skipped) and the
/// inspector (SpellSOEditor: HelpBox).</summary>
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

        AreaAttackAuthoring ownerArea = so.SpellPrefab != null ? so.SpellPrefab.GetComponent<AreaAttackAuthoring>() : null;
        for (int i = 0; i < entries.Length && i < MaxEmitters; i++)
        {
            string error = ValidateEntry(so, entries[i], ownerArea);
            if (error != null) errors.Add($"{so.name} emitter[{i}]: {error}");
            else valid[i] = true;
        }
        return valid;
    }

    private static string ValidateEntry(SpellSO so, SpellEmitterEntry e, AreaAttackAuthoring ownerArea)
    {
        if (e == null) return "entry is null.";
        if (e.Child == null) return "Child is null.";
        if (e.Capability != ESpellCapability.None && e.Capability != ESpellCapability.Explode
            && e.Capability != ESpellCapability.Split && e.Capability != ESpellCapability.Emit)
            return "Capability must be None, Explode, Split or Emit.";
        if ((e.Count == EEmitterCount.SplitCount) != (e.Capability == ESpellCapability.Split))
            return "Count = SplitCount requires Capability = Split, and a Split entry requires Count = SplitCount.";
        if (e.Capability == ESpellCapability.Explode
            && (e.Child.SpellPrefab == null || e.Child.SpellPrefab.GetComponent<AreaAttackAuthoring>() == null))
            return "an Explode child must be an AreaAttack Burst (AreaAttackAuthoring on its prefab).";
        // The runtime drops any attached request whose trigger is not OnActivation (SpellEmissionSystem, DroppedInvalid).
        if (e.Attachment == EEmitterAttachment.Attached && e.Trigger != EEmitterTrigger.OnActivation)
            return "Attached only supports Trigger = OnActivation.";
        if (e.Trigger == EEmitterTrigger.OnImpact && ownerArea != null)
            return "OnImpact on an Area spell is not supported.";
        if (e.Trigger == EEmitterTrigger.OnArm)
        {
            // OnArm = Burst only (D15): AreaAttackAuthoring only bakes Burst zones.
            if (ownerArea == null)
                return "OnArm requires an AreaAttack Burst owner (AreaAttackAuthoring on the SpellPrefab).";
            // D12: the carrier must live long enough to arm; its children are independent afterwards.
            if (!(so.Lifetime > ownerArea.ActivationDelay))
                return $"OnArm requires Lifetime ({so.Lifetime}) > ActivationDelay ({ownerArea.ActivationDelay}) of the prefab: the spell dies before arming.";
        }
        if (e.Timing == EEmitterTiming.Sequential)
        {
            if (!(e.SequenceInterval > 0f))
                return "Sequential requires SequenceInterval > 0.";
            if (e.Attachment != EEmitterAttachment.Detached)
                return "Sequential requires Attachment = Detached.";
            // D14: the PendingEmit ticket carries neither the explosion path nor the Split generation.
            if (e.Capability != ESpellCapability.None && e.Capability != ESpellCapability.Emit)
                return "Sequential is reserved to Capability None or Emit.";
        }
        if (e.Layout == EEmitterLayout.Line)
        {
            if (!(e.LineSpacing > 0f))
                return "Line requires LineSpacing > 0.";
            if (e.Attachment != EEmitterAttachment.Detached)
                return "Line requires Attachment = Detached.";
            if (e.Count == EEmitterCount.SplitCount)
                return "Line does not support Count = SplitCount.";
        }
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
