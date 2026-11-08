using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Keeps, for each Summon spell equipped by a Player, exactly FinalAmount agents alive (spec allegeance-summon §3):
/// spawns the missing slots at a free position of the ring around the owner (never on it), removes the surplus and the
/// groups whose spell is gone, relocates a Relocate agent at once when it reaches ExpiresAt. Work only happens on frames
/// where an owner recomputed its stats (Amount, spell added / removed), an agent's owner is gone, or an agent expired —
/// otherwise one read-only scan of Summon, no sync point. Spawn / destroy go through the EndSimulation ECB (agents live
/// next frame); relocation writes LocalTransform directly after CompleteDependency. Removing an agent also destroys its
/// PersistentInstance(s) (Beam emitter owner): EmitterReconcileSystem never cleans an owner whose caster is gone.
/// End of run: RunScope (RunCleanerSystem).
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(SpellStatsCalculationSystem))]
[UpdateBefore(typeof(SpellCastingSystem))]
[BurstCompile]
public partial struct SummonReconcileSystem : ISystem
{
    private EntityQuery _dirtyOwnerQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SpellsDatabase>();
        state.RequireForUpdate<SummonPrefab>();
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        _dirtyOwnerQuery = SystemAPI.QueryBuilder().WithAll<Player, SpellStatsCalculationRequest, ActiveSpell>().Build();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState) || gameState.State != EGameState.Running)
            return;

        double now = SystemAPI.Time.ElapsedTime;

        // Cheap trigger scan (Summon is written by nobody but this system: no job to complete).
        bool work = !_dirtyOwnerQuery.IsEmptyIgnoreFilter;
        if (!work)
        {
            foreach (var summon in SystemAPI.Query<RefRO<Summon>>())
            {
                if (now >= summon.ValueRO.ExpiresAt || !SystemAPI.Exists(summon.ValueRO.Owner))
                {
                    work = true;
                    break;
                }
            }
        }
        if (!work)
            return;

        // FinalAmount / FinalDuration were written this frame by SpellStatsCalculationSystem's job; transforms and the
        // collision world are read below. Rare frames only.
        state.CompleteDependency();

        var db = SystemAPI.GetSingleton<SpellsDatabase>().Blobs;
        ref var spells = ref db.Value.Spells;
        var prefabs = SystemAPI.GetSingletonBuffer<SummonPrefab>(true);
        var planet = SystemAPI.GetSingleton<PlanetData>();
        var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;
        var teamLookup = SystemAPI.GetComponentLookup<Team>(true);
        var playerLookup = SystemAPI.GetComponentLookup<Player>(true);
        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var rng = Random.CreateFromIndex((uint)(now * 1000) + 1);

        // 1) Agents by owner; orphans (owner gone) removed.
        var agentsByOwner = new NativeParallelMultiHashMap<Entity, Entity>(16, Allocator.Temp);
        var toDestroy = new NativeList<Entity>(Allocator.Temp);
        foreach (var (summon, agent) in SystemAPI.Query<RefRO<Summon>>().WithEntityAccess())
        {
            if (!SystemAPI.Exists(summon.ValueRO.Owner))
                toDestroy.Add(agent);
            else
                agentsByOwner.Add(summon.ValueRO.Owner, agent);
        }

        var owners = new NativeList<Entity>(Allocator.Temp);
        foreach (var (_, owner) in SystemAPI.Query<RefRO<Player>>().WithAll<ActiveSpell, LocalTransform>().WithEntityAccess())
            owners.Add(owner);

        var mine = new NativeList<Entity>(Allocator.Temp);
        var kept = new NativeList<Entity>(Allocator.Temp);
        var occupied = new NativeList<float>(Allocator.Temp);

        for (int o = 0; o < owners.Length; o++)
        {
            Entity owner = owners[o];
            var ownerSpells = SystemAPI.GetBuffer<ActiveSpell>(owner);
            var ownerTransform = SystemAPI.GetComponent<LocalTransform>(owner);
            float3 ownerPos = ownerTransform.Position;
            float3 n = math.normalizesafe(ownerPos - planet.Center, new float3(0f, 1f, 0f));
            float3 reference = EmitterMath.TangentReference(n, math.forward(ownerTransform.Rotation));
            ETeam ownerTeam = TeamUtils.Resolve(owner, in teamLookup, in playerLookup);

            mine.Clear();
            if (agentsByOwner.TryGetFirstValue(owner, out var a, out var it))
            {
                do mine.Add(a);
                while (agentsByOwner.TryGetNextValue(out a, ref it));
            }

            // a) Remove the agents whose spell is gone or whose slot is beyond FinalAmount.
            kept.Clear();
            for (int i = 0; i < mine.Length; i++)
            {
                var s = SystemAPI.GetComponent<Summon>(mine[i]);
                if (!TryGetSummonSpell(ownerSpells, ref spells, s.SourceDatabaseIndex, out var src) || s.Slot >= src.FinalAmount)
                    toDestroy.Add(mine[i]);
                else
                    kept.Add(mine[i]);
            }

            occupied.Clear();
            for (int i = 0; i < kept.Length; i++)
                occupied.Add(SummonMath.RingAngle(SystemAPI.GetComponent<LocalTransform>(kept[i]).Position - ownerPos, n, reference));

            // b) Relocate the expired Relocate agents, at once, to a free point of the ring (spec §3: no dead time).
            for (int i = 0; i < kept.Length; i++)
            {
                var s = SystemAPI.GetComponent<Summon>(kept[i]);
                ref var sb = ref spells[s.SourceDatabaseIndex].Summon;
                if (sb.EndOfLife != ESummonEndOfLife.Relocate || now < s.ExpiresAt)
                    continue;
                TryGetSummonSpell(ownerSpells, ref spells, s.SourceDatabaseIndex, out var src);

                occupied.RemoveAtSwapBack(IndexOfAngle(in occupied, SummonMath.RingAngle(
                    SystemAPI.GetComponent<LocalTransform>(kept[i]).Position - ownerPos, n, reference)));
                float angle = SummonMath.FreeRingAngle(occupied.AsArray());
                float radius = math.lerp(sb.RingMinRadius, sb.RingMaxRadius, rng.NextFloat());
                float scale = SystemAPI.GetComponent<LocalTransform>(kept[i]).Scale;
                SystemAPI.SetComponent(kept[i], PlaceOnRing(ownerPos, n, reference, angle, radius, in planet, ref collisionWorld, scale));
                occupied.Add(angle);

                s.ExpiresAt = now + src.FinalDuration;
                SystemAPI.SetComponent(kept[i], s);
            }

            // c) Spawn the missing slots of every equipped Summon spell.
            for (int k = 0; k < ownerSpells.Length; k++)
            {
                int dbIndex = ownerSpells[k].DatabaseIndex;
                ref var spellBlob = ref spells[dbIndex];
                if (!spellBlob.IsSummon || dbIndex >= prefabs.Length || prefabs[dbIndex].Prefab == Entity.Null)
                    continue;
                ref var sb = ref spellBlob.Summon;
                Entity prefab = prefabs[dbIndex].Prefab;
                float scale = SystemAPI.GetComponent<LocalTransform>(prefab).Scale;
                var src = ownerSpells[k];

                for (int slot = 0; slot < src.FinalAmount; slot++)
                {
                    if (HasSlot(ref state, in kept, dbIndex, slot))
                        continue;

                    float angle = SummonMath.FreeRingAngle(occupied.AsArray());
                    float radius = math.lerp(sb.RingMinRadius, sb.RingMaxRadius, rng.NextFloat());
                    occupied.Add(angle);

                    Entity agent = ecb.Instantiate(prefab);
                    ecb.SetComponent(agent, PlaceOnRing(ownerPos, n, reference, angle, radius, in planet, ref collisionWorld, scale));
                    ecb.SetComponent(agent, new Summon
                    {
                        Owner = owner,
                        SourceDatabaseIndex = dbIndex,
                        Slot = slot,
                        ExpiresAt = sb.EndOfLife == ESummonEndOfLife.Relocate ? now + src.FinalDuration : double.MaxValue,
                    });
                    ecb.SetComponent(agent, new Team { Value = ownerTeam });
                    for (int s = 0; s < sb.AttackSpellIndices.Length; s++)
                        ecb.AppendToBuffer(agent, new SpellActivationRequest { ID = spells[sb.AttackSpellIndices[s]].ID });
                }
            }
        }

        // 2) Removals: the agent and the persistent instances it owns (Beam owner + its rays through the LinkedEntityGroup).
        for (int i = 0; i < toDestroy.Length; i++)
        {
            Entity agent = toDestroy[i];
            if (SystemAPI.HasBuffer<ActiveSpell>(agent))
            {
                var agentSpells = SystemAPI.GetBuffer<ActiveSpell>(agent);
                for (int k = 0; k < agentSpells.Length; k++)
                {
                    Entity persistent = agentSpells[k].PersistentInstance;
                    if (persistent != Entity.Null && SystemAPI.Exists(persistent))
                        ecb.DestroyEntity(persistent);
                }
            }
            ecb.DestroyEntity(agent);
        }

        occupied.Dispose();
        kept.Dispose();
        mine.Dispose();
        owners.Dispose();
        toDestroy.Dispose();
        agentsByOwner.Dispose();
    }

    private static bool TryGetSummonSpell(DynamicBuffer<ActiveSpell> ownerSpells, ref BlobArray<SpellBlob> spells, int dbIndex, out ActiveSpell spell)
    {
        spell = default;
        if (dbIndex < 0 || dbIndex >= spells.Length || !spells[dbIndex].IsSummon)
            return false;
        for (int i = 0; i < ownerSpells.Length; i++)
        {
            if (ownerSpells[i].DatabaseIndex != dbIndex)
                continue;
            spell = ownerSpells[i];
            return true;
        }
        return false;
    }

    private bool HasSlot(ref SystemState state, in NativeList<Entity> agents, int dbIndex, int slot)
    {
        for (int i = 0; i < agents.Length; i++)
        {
            var s = SystemAPI.GetComponent<Summon>(agents[i]);
            if (s.SourceDatabaseIndex == dbIndex && s.Slot == slot)
                return true;
        }
        return false;
    }

    private static int IndexOfAngle(in NativeList<float> angles, float angle)
    {
        int best = 0;
        float bestDelta = float.MaxValue;
        for (int i = 0; i < angles.Length; i++)
        {
            float d = math.abs(angles[i] - angle);
            if (d < bestDelta) { bestDelta = d; best = i; }
        }
        return best;
    }

    /// <summary>Ring point around the owner, snapped to the terrain (Landscape ray, half-length derived from the planet
    /// radius), facing away from the owner.</summary>
    private static LocalTransform PlaceOnRing(float3 ownerPos, float3 n, float3 reference, float angle, float radius,
        in PlanetData planet, ref CollisionWorld collisionWorld, float scale)
    {
        float3 p = ownerPos + EmitterMath.OrbitOffset(n, reference, radius, angle);
        var filter = new CollisionFilter { BelongsTo = CollisionLayers.Raycast, CollidesWith = CollisionLayers.Landscape };
        float probe = math.max(1f, planet.Radius * 0.5f);
        float3 grounded;
        if (PlanetUtils.SnapToSurfaceRaycast(ref collisionWorld, in p, in planet.Center, in filter, probe, out var hit))
            grounded = hit.Position;
        else
            PlanetUtils.SnapToSurfaceRadius(in p, in planet.Center, math.length(ownerPos - planet.Center), out grounded);

        float3 up = math.normalizesafe(grounded - planet.Center, n);
        float3 facing = EmitterMath.TangentReference(up, grounded - ownerPos);
        return LocalTransform.FromPositionRotationScale(grounded, quaternion.LookRotationSafe(facing, up), scale);
    }
}
