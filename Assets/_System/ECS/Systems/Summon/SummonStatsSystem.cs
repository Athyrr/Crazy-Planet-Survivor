using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

/// <summary>
/// Agent stats (spec allegeance-summon §2-3), on frames where the agent or its owner carries SpellStatsCalculationRequest:
///   • CoreStats = SummonMath.ComposeAgentCoreStats(SummonSO base, ratios, owner CoreStats) — inheritance per stat kind,
///     Damage × (1 + owner SummonPower). Only the owner's CoreStats are read: its temporary CharacterStatBuff entries
///     (SummonPower included) are not inherited;
///   • each attack ActiveSpell of the agent receives the RAW upgrades of the owner's Summon spell (Local*, AddedEffects,
///     AddedTags without form bits, GrantedCapabilities), never its Final* (they already hold the owner's CoreStats);
///     Amount and Duration are excluded (consumed by the Summon);
///   • the agent's SpellStatUpgrade buffer = the owner's entries tagged Summon (RequiredTags → All, without Amount /
///     Duration); every other tagged upgrade of the player stays out (no leak: the player influences the agent only
///     through its character stats and SummonPower).
/// SpellStatsCalculationSystem (generic) then recomputes the agent's Final*: in the same frame when the agent already
/// carried the request (spawn), one frame later otherwise (request added through the EndSimulation ECB).
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(SpellStatsCalculationSystem))]
[BurstCompile]
public partial struct SummonStatsSystem : ISystem
{
    private const ESpellTag FormBits = ESpellTag.Ranged | ESpellTag.Melee | ESpellTag.Projectile | ESpellTag.Area
                                       | ESpellTag.Summon | ESpellTag.Buff | ESpellTag.Debuff;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Summon>();
        state.RequireForUpdate<SpellsDatabase>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var dirty = new NativeList<Entity>(Allocator.Temp);
        foreach (var (summon, agent) in SystemAPI.Query<RefRO<Summon>>().WithEntityAccess())
        {
            if (SystemAPI.HasComponent<SpellStatsCalculationRequest>(agent)
                || SystemAPI.HasComponent<SpellStatsCalculationRequest>(summon.ValueRO.Owner))
                dirty.Add(agent);
        }
        if (dirty.Length == 0)
        {
            dirty.Dispose();
            return;
        }

        // Main-thread reads / writes of CoreStats, ActiveSpell and SpellStatUpgrade below. Stat-change frames only.
        state.CompleteDependency();

        var db = SystemAPI.GetSingleton<SpellsDatabase>().Blobs;
        ref var spells = ref db.Value.Spells;
        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var summonMods = new NativeList<SpellStatUpgrade>(Allocator.Temp);

        for (int d = 0; d < dirty.Length; d++)
        {
            Entity agent = dirty[d];
            var summon = SystemAPI.GetComponent<Summon>(agent);
            Entity owner = summon.Owner;
            if (!SystemAPI.Exists(owner) || !SystemAPI.HasComponent<CoreStats>(owner) || !SystemAPI.HasBuffer<ActiveSpell>(owner))
                continue;
            if (summon.SourceDatabaseIndex < 0 || summon.SourceDatabaseIndex >= spells.Length)
                continue;
            ref var summonSpellBlob = ref spells[summon.SourceDatabaseIndex];
            if (!summonSpellBlob.IsSummon)
                continue;

            // Owner's Summon spell (raw upgrades). Gone → SummonReconcileSystem removes the agent.
            var ownerSpells = SystemAPI.GetBuffer<ActiveSpell>(owner);
            ActiveSpell summonSpell = default;
            bool found = false;
            for (int i = 0; i < ownerSpells.Length; i++)
            {
                if (ownerSpells[i].DatabaseIndex != summon.SourceDatabaseIndex)
                    continue;
                summonSpell = ownerSpells[i];
                found = true;
                break;
            }
            if (!found)
                continue;

            var ownerCore = SystemAPI.GetComponent<CoreStats>(owner);
            SystemAPI.SetComponent(agent, SummonMath.ComposeAgentCoreStats(in summonSpellBlob.Summon.BaseStats,
                in summonSpellBlob.Summon.Ratios, in ownerCore));

            var agentSpells = SystemAPI.GetBuffer<ActiveSpell>(agent);
            for (int k = 0; k < agentSpells.Length; k++)
            {
                var attack = agentSpells[k];
                ProjectSummonUpgrades(ref attack, in summonSpell, spells[attack.DatabaseIndex].BaseGrantedCapabilities);
                agentSpells[k] = attack;
            }

            // Owner entries copied out first: Clear / Add on the agent's buffer bump the array-invalidation handle shared
            // by every SpellStatUpgrade buffer of the lookup, which would invalidate the owner's buffer mid-read.
            summonMods.Clear();
            if (SystemAPI.HasBuffer<SpellStatUpgrade>(owner))
            {
                var ownerMods = SystemAPI.GetBuffer<SpellStatUpgrade>(owner);
                for (int m = 0; m < ownerMods.Length; m++)
                {
                    var mod = ownerMods[m];
                    if ((mod.RequiredTags & ESpellTag.Summon) == 0
                        || mod.SpellStat == ESpellStat.Amount || mod.SpellStat == ESpellStat.Duration)
                        continue;
                    mod.RequiredTags = ESpellTag.All;
                    summonMods.Add(mod);
                }
            }
            var agentMods = SystemAPI.GetBuffer<SpellStatUpgrade>(agent);
            agentMods.Clear();
            agentMods.AddRange(summonMods.AsArray());

            if (!SystemAPI.HasComponent<SpellStatsCalculationRequest>(agent))
                ecb.AddComponent<SpellStatsCalculationRequest>(agent);
        }

        summonMods.Dispose();
        dirty.Dispose();
    }

    /// <summary>Raw upgrades of the owner's Summon spell → one attack ActiveSpell of the agent. Overwrites (the attack copy
    /// is never upgraded on its own). Excluded: LocalAmountBonus (number of agents) and LocalSpellDurationBonusMultiplier
    /// (time before relocation) — no double effect (spec §2, validated 2026-11-08).</summary>
    internal static void ProjectSummonUpgrades(ref ActiveSpell attack, in ActiveSpell summonSpell, ESpellCapability attackBaseGranted)
    {
        attack.Level = summonSpell.Level;
        attack.LocalDamageBonusMultiplier = summonSpell.LocalDamageBonusMultiplier;
        attack.LocalSizeBonusMultiplier = summonSpell.LocalSizeBonusMultiplier;
        attack.LocalSpeedBonusMultiplier = summonSpell.LocalSpeedBonusMultiplier;
        attack.LocalCooldownReducBonusMultiplier = summonSpell.LocalCooldownReducBonusMultiplier;
        attack.LocalRangeBonusMultiplier = summonSpell.LocalRangeBonusMultiplier;
        attack.LocalTickRateBonusMultiplier = summonSpell.LocalTickRateBonusMultiplier;
        attack.LocalBounceRangeBonusMultiplier = summonSpell.LocalBounceRangeBonusMultiplier;
        attack.LocalBounceBonus = summonSpell.LocalBounceBonus;
        attack.LocalPierceBonus = summonSpell.LocalPierceBonus;
        attack.LocalExplosionSizeBonus = summonSpell.LocalExplosionSizeBonus;
        attack.LocalCritChanceBonusPercent = summonSpell.LocalCritChanceBonusPercent;
        attack.LocalCritDamageBonus = summonSpell.LocalCritDamageBonus;
        attack.LocalLifeStealChanceBonus = summonSpell.LocalLifeStealChanceBonus;
        attack.LocalBurnMagnitudeBonus = summonSpell.LocalBurnMagnitudeBonus;
        attack.LocalSlowMagnitudeBonus = summonSpell.LocalSlowMagnitudeBonus;
        attack.LocalSplitCount = summonSpell.LocalSplitCount;
        attack.LocalSplitDepth = summonSpell.LocalSplitDepth;
        attack.AddedTags = summonSpell.AddedTags & ~FormBits;
        attack.AddedEffects = summonSpell.AddedEffects;
        // Allow* of the attack spell still decide (SpellStatsCalculationSystem derives Bounce / Pierce / Explode from
        // its AllowedCapabilities; Split needs its emitter entry): an unallowed capability is simply ignored.
        attack.GrantedCapabilities = attackBaseGranted | summonSpell.GrantedCapabilities;
    }
}
