using Unity.Collections;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

[UpdateInGroup(typeof(FixedStepSimulationSystemGroup))]
[UpdateAfter(typeof(CollisionSystem))]
[BurstCompile]
public partial struct ExplosionSystem : ISystem
{
    private ComponentLookup<LocalTransform> _transformLookup;
    private ComponentLookup<AreaAttack> _areaAttackLookup;
    private ComponentLookup<SpellSource> _spellSourceLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndFixedStepSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<PhysicsWorldSingleton>();

        _transformLookup = state.GetComponentLookup<LocalTransform>(true);
        _areaAttackLookup = state.GetComponentLookup<AreaAttack>(true);
        _spellSourceLookup = state.GetComponentLookup<SpellSource>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton(out GameState gameState) || gameState.State != EGameState.Running)
            return;

        _transformLookup.Update(ref state);
        _areaAttackLookup.Update(ref state);
        _spellSourceLookup.Update(ref state);

        var ecbSingleton = SystemAPI.GetSingleton<EndFixedStepSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var job = new ProcessExplosionsJob
        {
            ECB = ecb.AsParallelWriter(),
            TransformLookup = _transformLookup,
            AreaAttackLookup = _areaAttackLookup,
            SpellSourceLookup = _spellSourceLookup,
        };

        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    public partial struct ProcessExplosionsJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        [ReadOnly] public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public ComponentLookup<AreaAttack> AreaAttackLookup;
        [ReadOnly] public ComponentLookup<SpellSource> SpellSourceLookup;

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, in ExplosionRequest explosion)
        {
            if (explosion.VfxPrefab != Entity.Null && AreaAttackLookup.HasComponent(explosion.VfxPrefab))
            {
                // Shared prefab, low-level helper (Q13). Enemy requests carry DatabaseIndex -1 → no SpellSource
                // attribution (Caster Null): no tracking, no life steal.
                bool hasSource = explosion.DatabaseIndex >= 0;
                SpellSpawnUtils.SpawnBurstArea(in TransformLookup, in AreaAttackLookup, in SpellSourceLookup, ECB, chunkIndex,
                    explosion.VfxPrefab, explosion.Position, quaternion.identity, 1f,
                    explosion.Damage, 0f, 0f, explosion.Tags, explosion.TargetLayers,
                    hasSource ? explosion.Damager : Entity.Null, hasSource ? explosion.DatabaseIndex : -1);
            }

            ECB.DestroyEntity(chunkIndex, entity);
        }
    }
}

/// <summary>
/// Request for an explosion, turned into an AreaAttack Burst by <see cref="ExplosionSystem"/>: enemy
/// ExplodeOnDeath (and, until Task 16, the legacy player ExplodeOnContact); DatabaseIndex -1 = no source.
/// </summary>
public struct ExplosionRequest : IComponentData
{
    public float3 Position;
    public float Damage;
    public ESpellTag Tags;
    public bool IsCritical;
    public Entity VfxPrefab;

    public uint TargetLayers;
    public int DatabaseIndex;

    public Entity Damager;
    public bool IsPlayer;
}