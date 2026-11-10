#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections.Generic;
using Unity.Entities;

/// <summary>Spell commands. &lt;spell&gt; = ESpellID name (exact, then substring) or numeric value.</summary>
public static class SandboxSpellCommands
{
    [ConsoleCommand("add_spell", "Ajoute un spell au joueur, niveau 1 (nom ou valeur d'ESpellID).")]
    private static string AddSpell(string spell)
    {
        if (!SandboxCommandUtils.TryResolveSpellId(spell, out var id, out var error))
            return error;
        if (!SandboxCommandUtils.TryGetPlayer(out var em, out var player))
            return SandboxCommandUtils.NoPlayer;
        int dbIndex = SandboxCommandUtils.FindSpellDbIndex(em, id);
        if (dbIndex < 0)
            return $"{id} absent de la base de spells.";
        if (SandboxCommandUtils.FindActiveSpell(em.GetBuffer<ActiveSpell>(player, true), dbIndex) >= 0)
            return $"{id} est déjà actif.";

        em.GetBuffer<SpellActivationRequest>(player).Add(new SpellActivationRequest { ID = id });
        return $"{id} ajouté (niveau 1).";
    }

    [ConsoleCommand("remove_spell", "Retire un spell du joueur et détruit ses projectiles, zones, orbes, rayons (summons : par SummonReconcileSystem).")]
    private static string RemoveSpell(string spell)
    {
        if (!SandboxCommandUtils.TryResolveSpellId(spell, out var id, out var error))
            return error;
        if (!SandboxCommandUtils.TryGetPlayer(out var em, out var player))
            return SandboxCommandUtils.NoPlayer;
        int dbIndex = SandboxCommandUtils.FindSpellDbIndex(em, id);
        int destroyed = dbIndex < 0 ? -1 : RemoveSpellCore(em, player, dbIndex);
        return destroyed < 0 ? $"{id} n'est pas actif." : $"{id} retiré ({destroyed} instance(s) détruite(s)).";
    }

    [ConsoleCommand("clear_spells", "Retire tous les spells du joueur, spell de départ compris.")]
    private static string ClearSpells()
    {
        if (!SandboxCommandUtils.TryGetPlayer(out var em, out var player))
            return SandboxCommandUtils.NoPlayer;
        var indices = new List<int>();
        foreach (var s in em.GetBuffer<ActiveSpell>(player, true))
            indices.Add(s.DatabaseIndex);
        int destroyed = 0;
        foreach (var index in indices)
            destroyed += Math.Max(0, RemoveSpellCore(em, player, index));
        return $"{indices.Count} spell(s) retiré(s), {destroyed} instance(s) détruite(s).";
    }

    [ConsoleCommand("spell_cap", "Allume / éteint Explode ou Emit sur un spell actif (tirs suivants).")]
    private static string SpellCap(string spell, string capability, bool on)
    {
        ESpellCapability cap;
        if (string.Equals(capability, "Explode", StringComparison.OrdinalIgnoreCase)) cap = ESpellCapability.Explode;
        else if (string.Equals(capability, "Emit", StringComparison.OrdinalIgnoreCase)) cap = ESpellCapability.Emit;
        else return "Capacité : Explode ou Emit.";

        if (!TryGetActive(spell, out var em, out var player, out var id, out var dbIndex, out var slot, out var error))
            return error;
        if (!IsAllowed(em, dbIndex, cap))
            return $"{id} n'a pas le droit {cap} (entrée d'émetteur absente du SpellSO) : rien changé.";

        var active = em.GetBuffer<ActiveSpell>(player);
        var s = active[slot];
        s.GrantedCapabilities = on ? s.GrantedCapabilities | cap : s.GrantedCapabilities & ~cap;
        active[slot] = s;
        em.AddComponent<SpellStatsCalculationRequest>(player);

        var o = SandboxSession.GetOrAddOverride(id);
        if (cap == ESpellCapability.Explode) o.Explode = on ? 1 : 0; else o.Emit = on ? 1 : 0;
        return $"{id} : {cap} {(on ? "on" : "off")}.";
    }

    [ConsoleCommand("spell_bounce", "Fixe le nombre de rebonds d'un spell actif (s'il a le droit et le slot).")]
    private static string SpellBounce(string spell, int count) => SetCounter(spell, count, ESpellCapability.Bounce);

    [ConsoleCommand("spell_pierce", "Fixe le nombre de perforations d'un spell actif (s'il a le droit et le slot).")]
    private static string SpellPierce(string spell, int count) => SetCounter(spell, count, ESpellCapability.Pierce);

    private static string SetCounter(string spell, int count, ESpellCapability cap)
    {
        if (count < 0)
            return "n doit être >= 0.";
        if (!TryGetActive(spell, out var em, out var player, out var id, out var dbIndex, out var slot, out var error))
            return error;
        // Bounce/Pierce allowed = SpellSO permission AND prefab slot (baked in SpellBlob.AllowedCapabilities).
        if (!IsAllowed(em, dbIndex, cap))
            return $"{id} n'a pas le droit ou le slot {cap} : rien changé.";
        if (em.HasComponent<SpellStatsCalculationRequest>(player))
            return "Recalcul des stats en cours, réessaie à la frame suivante.";

        // Final = base + CoreStats + tagged upgrades + local: shift the local bonus by the gap (may go negative).
        var active = em.GetBuffer<ActiveSpell>(player);
        var s = active[slot];
        if (cap == ESpellCapability.Bounce) s.LocalBounceBonus += count - s.FinalBounces;
        else s.LocalPierceBonus += count - s.FinalPierces;
        active[slot] = s;
        em.AddComponent<SpellStatsCalculationRequest>(player);

        var o = SandboxSession.GetOrAddOverride(id);
        if (cap == ESpellCapability.Bounce) o.Bounce = count; else o.Pierce = count;
        return $"{id} : {cap} fixé à {count}.";
    }

    /// <summary>Removes the ActiveSpell at dbIndex, destroys its live instances, requests a stat pass. -1 = not active.</summary>
    internal static int RemoveSpellCore(EntityManager em, Entity player, int dbIndex)
    {
        var active = em.GetBuffer<ActiveSpell>(player);
        int slot = SandboxCommandUtils.FindActiveSpell(active, dbIndex);
        if (slot < 0)
            return -1;
        active.RemoveAt(slot); // handle not reused after the structural changes below

        int destroyed = SandboxCommandUtils.DestroySpellInstances(em, player, dbIndex);
        em.AddComponent<SpellStatsCalculationRequest>(player);
        if (SandboxCommandUtils.TryGetSpellId(em, dbIndex, out var id))
            SandboxSession.ForgetSpell(id);
        return destroyed;
    }

    private static bool TryGetActive(string spell, out EntityManager em, out Entity player, out ESpellID id,
        out int dbIndex, out int slot, out string error)
    {
        em = default; player = Entity.Null; dbIndex = -1; slot = -1;
        if (!SandboxCommandUtils.TryResolveSpellId(spell, out id, out error))
            return false;
        if (!SandboxCommandUtils.TryGetPlayer(out em, out player))
        {
            error = SandboxCommandUtils.NoPlayer;
            return false;
        }
        dbIndex = SandboxCommandUtils.FindSpellDbIndex(em, id);
        slot = dbIndex < 0 ? -1 : SandboxCommandUtils.FindActiveSpell(em.GetBuffer<ActiveSpell>(player, true), dbIndex);
        if (slot < 0)
        {
            error = $"{id} n'est pas actif.";
            return false;
        }
        return true;
    }

    private static bool IsAllowed(EntityManager em, int dbIndex, ESpellCapability cap)
    {
        if (!SandboxCommandUtils.TryGetSpellsDb(em, out var db))
            return false;
        ref var blob = ref db.Value.Spells[dbIndex];
        return (blob.AllowedCapabilities & cap) != 0;
    }
}
#endif
