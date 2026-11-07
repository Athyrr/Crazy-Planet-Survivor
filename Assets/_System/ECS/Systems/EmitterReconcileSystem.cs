using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>Live re-sync of attached emitter owners (FireOrbs) with their root ActiveSpell: runs only on the frames
/// where the caster carries a SpellStatsCalculationRequest (removed at EndSimulation). Refreshes angular speed,
/// radius and child scale, and adds / removes anchored children when the resolved count changes, rewriting the
/// owner's LinkedEntityGroup (owner first) so a later group destroy never targets a dead entity.</summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(SpellStatsCalculationSystem))]
[UpdateBefore(typeof(SpellCastingSystem))]
[BurstCompile]
public partial struct EmitterReconcileSystem : ISystem
{
    private SpellSpawnLookups _spawn;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EmitterOwner>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        _spawn = SpellSpawnLookups.Create(ref state);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<SpellsDatabase>(out var db) || !SystemAPI.HasSingleton<SpellPrefab>())
            return;

        // 1) Owners whose caster recomputed its stats this frame (request removed at EndSimulation).
        var dirtyOwners = new NativeList<Entity>(Allocator.Temp);
        foreach (var (owner, source, ownerEntity) in SystemAPI.Query<RefRO<EmitterOwner>, RefRO<SpellSource>>().WithEntityAccess())
            if (SystemAPI.HasComponent<SpellStatsCalculationRequest>(source.ValueRO.CasterEntity))
                dirtyOwners.Add(ownerEntity);
        if (dirtyOwners.Length == 0) { dirtyOwners.Dispose(); return; }

        // Main-thread reads below (ActiveSpell written by SpellStatsCalculationSystem's job this frame, caster
        // LocalTransform, anchors): complete the jobs this system depends on. Rare: stat-change frames only.
        state.CompleteDependency();

        // 2) Current anchors, per owner.
        var anchorsByOwner = new NativeParallelMultiHashMap<Entity, Entity>(64, Allocator.Temp);
        foreach (var (anchor, anchorEntity) in SystemAPI.Query<RefRO<EmitterAnchor>>().WithEntityAccess())
            anchorsByOwner.Add(anchor.ValueRO.Owner, anchorEntity);

        _spawn.Update(ref state);
        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        var pecb = ecb.AsParallelWriter();
        var prefabs = SystemAPI.GetSingletonBuffer<SpellPrefab>(true);
        var activeSpellLookup = SystemAPI.GetBufferLookup<ActiveSpell>(true);
        ref var spells = ref db.Blobs.Value.Spells;

        foreach (var ownerEntity in dirtyOwners)
        {
            var owner = SystemAPI.GetComponent<EmitterOwner>(ownerEntity);
            var source = SystemAPI.GetComponent<SpellSource>(ownerEntity);
            if (source.DatabaseIndex < 0 || source.DatabaseIndex >= spells.Length) continue;
            ref var rootBlob = ref spells[source.DatabaseIndex];
            if (owner.EmitterIndex < 0 || owner.EmitterIndex >= rootBlob.Emitters.Length) continue;
            ref var em = ref rootBlob.Emitters[owner.EmitterIndex];

            // Root spell gone → destroy the owner (its LinkedEntityGroup takes the anchors).
            if (!EmitterQueries.TryGetActiveSpell(activeSpellLookup, source.CasterEntity, source.DatabaseIndex, out var root))
            {
                pecb.DestroyEntity(0, ownerEntity);
                continue;
            }

            int newCount = EmitterQueries.ResolveCount(ref em, in root);
            owner.AngularSpeed = math.radians(root.FinalSpeed);
            owner.Radius = EmitterMath.OrbitRadius(em.Radius, root.FinalRange, rootBlob.BaseCastRange);
            owner.ChildScale = owner.ChildBaseScale * root.FinalSize;

            if (newCount != owner.Count)
            {
                // Rebuild the LinkedEntityGroup: owner first, kept anchors (Index < newCount), new anchors.
                var leg = pecb.SetBuffer<LinkedEntityGroup>(0, ownerEntity);
                leg.Add(new LinkedEntityGroup { Value = ownerEntity });
                if (anchorsByOwner.TryGetFirstValue(ownerEntity, out var a, out var it))
                {
                    do
                    {
                        if (SystemAPI.GetComponent<EmitterAnchor>(a).Index < newCount) leg.Add(new LinkedEntityGroup { Value = a });
                        else pecb.DestroyEntity(0, a);
                    } while (anchorsByOwner.TryGetNextValue(out a, ref it));
                }

                bool validChild = em.ChildIndex >= 0 && em.ChildIndex < prefabs.Length && prefabs[em.ChildIndex].Prefab != Entity.Null;
                if (validChild)
                {
                    uint layers = SystemAPI.HasComponent<Player>(source.CasterEntity)
                        ? CollisionLayers.Enemy | CollisionLayers.Obstacle
                        : CollisionLayers.Player | CollisionLayers.Obstacle;
                    float3 casterPos = SystemAPI.GetComponent<LocalTransform>(source.CasterEntity).Position;
                    for (int i = owner.Count; i < newCount; i++)
                    {
                        // SpawnAnchoredChild appends to the LEG through the same ParallelWriter and sortKey 0, recorded
                        // after the SetBuffer above: playback keeps that order, deferred children are remapped.
                        AttachedEmitterUtils.SpawnAnchoredChild(in _spawn, pecb, 0, ownerEntity, prefabs[em.ChildIndex].Prefab, i,
                            source.CasterEntity, source.DatabaseIndex, casterPos, layers);
                    }
                }
                owner.Count = newCount;
            }

            SystemAPI.SetComponent(ownerEntity, owner);
        }

        anchorsByOwner.Dispose();
        dirtyOwners.Dispose();
    }
}
