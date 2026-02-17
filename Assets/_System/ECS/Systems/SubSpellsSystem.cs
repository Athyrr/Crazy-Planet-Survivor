using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Represents a system that handle spells that spawn child entities with specific layouts (ex: Circle layout, ...).
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(TransformSystemGroup))]
[BurstCompile]
public partial struct SubSpellsSystem : ISystem
{
    private ComponentLookup<LocalTransform> _transformLookup;
    private BufferLookup<Child> _childBufferLookup;
    private ComponentLookup<PhysicsCollider> _colliderLookup;
    private ComponentLookup<DamageOnContact> _damageLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameState>();
        state.RequireForUpdate<SubSpellsSpawner>();

        _transformLookup = state.GetComponentLookup<LocalTransform>(false);
        _childBufferLookup = state.GetBufferLookup<Child>(false);
        _colliderLookup = state.GetComponentLookup<PhysicsCollider>(false);
        _damageLookup = state.GetComponentLookup<DamageOnContact>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Get game state
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        // Only run when game is running
        if (gameState.State != EGameState.Running)
            return;

        // Update lookups
        _transformLookup.Update(ref state);
        _childBufferLookup.Update(ref state);
        _colliderLookup.Update(ref state);
        _damageLookup.Update(ref state);

        // Setup ECB    
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        foreach (var (spawner, parentEntity) in
                         SystemAPI.Query<RefRW<SubSpellsSpawner>>().WithEntityAccess())
        {
            DamageOnContact parentDamage = default;
            bool hasDamage = _damageLookup.HasComponent(parentEntity);
            if (hasDamage)
            {
                parentDamage = _damageLookup[parentEntity];
            }

            int desiredCount = spawner.ValueRO.DesiredSubSpellsCount;
            int currentCount = 0;
            bool hasChildren = _childBufferLookup.HasBuffer(parentEntity);

            if (hasChildren)
            {
                currentCount = _childBufferLookup[parentEntity].Length;
            }
            else
            {
                ecb.AddBuffer<Child>(parentEntity);
            }

            if (!spawner.ValueRO.IsDirty)
                continue;

            // child missing
            if (currentCount < desiredCount)
            {
                for (int i = currentCount; i < desiredCount; i++)
                {
                    var childEntity = ecb.Instantiate(spawner.ValueRO.ChildEntityPrefab);

                    //ecb.AddComponent(childEntity, new LocalTransform { Scale = 1, Rotation = quaternion.identity });
                    ecb.AddComponent(childEntity, new LocalTransform { Scale = 0, Rotation = quaternion.identity });
                    ecb.AddComponent(childEntity, new Parent { Value = parentEntity });

                    ecb.AppendToBuffer(parentEntity, new Child { Value = childEntity });

                    // Collisions
                    if (_colliderLookup.HasComponent(spawner.ValueRO.ChildEntityPrefab))
                    {
                        var collider = _colliderLookup[spawner.ValueRO.ChildEntityPrefab];
                        collider.Value.Value.SetCollisionFilter(spawner.ValueRO.CollisionFilter);
                        ecb.SetComponent(childEntity, collider);
                    }

                    // Damages
                    if (hasDamage)
                    {
                        ecb.SetComponent(childEntity, parentDamage);
                    }
                }
                spawner.ValueRW.IsDirty = false;
            }

            // extra children
            else if (hasChildren && currentCount > desiredCount)
            {
                var children = _childBufferLookup[parentEntity];
                for (int i = currentCount - 1; i >= desiredCount; i--)
                {
                    ecb.DestroyEntity(children[i].Value);
                }

                // Create buffer via ecb
                var childrenBuffer = ecb.SetBuffer<Child>(parentEntity);
                for (int i = 0; i < desiredCount; i++)
                    childrenBuffer.Add(children[i]);

                spawner.ValueRW.IsDirty = true;
            }
        }

        var circleLayoutJob = new SubSpellsCircleLayoutJob()
        {
            ECB = ecb.AsParallelWriter(),
            TransformLookup = _transformLookup,
        };
        state.Dependency = circleLayoutJob.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(SubSpellsLayout_Circle))]
    private partial struct SubSpellsCircleLayoutJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        [NativeDisableParallelForRestriction]
        public ComponentLookup<LocalTransform> TransformLookup;

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity parentEntity, in SubSpellsLayout_Circle circleLayout, DynamicBuffer<Child> children)
        {
            if (children.IsEmpty)
                return;

            float angleStep = circleLayout.AngleInDegrees / children.Length;

            float currentAngle = 0f;

            // Position children in circle layout
            for (int i = 0; i < children.Length; i++)
            {
                var childEntity = children[i].Value;

                if (!TransformLookup.HasComponent(childEntity))
                {
                    currentAngle += angleStep;
                    continue;
                }

                float angleRad = math.radians(currentAngle);

                float3 localOffset = new float3(
                    circleLayout.Radius * math.sin(angleRad), // X
                    1f,
                    circleLayout.Radius * math.cos(angleRad)  // Z
                );

                //float3 localOffset = new float3(
                //    circleLayout.Radius * math.cos(currentAngle),
                //    0f,
                //    circleLayout.Radius * math.sin(currentAngle)
                //);

                var childTransform = TransformLookup[childEntity];

                childTransform.Position = localOffset;
                childTransform.Rotation = quaternion.LookRotationSafe(math.normalize(localOffset), math.up());
                childTransform.Scale = 1f;

                TransformLookup[childEntity] = childTransform;

                currentAngle += angleStep;
            }
        }
    }
}
