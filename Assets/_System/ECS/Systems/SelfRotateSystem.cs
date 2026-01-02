using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe; // <--- INDISPENSABLE POUR L'ATTRIBUT
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

[BurstCompile]
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct SelfRotateSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SelfRotate>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (gameState.State != EGameState.Running)
            return;

        if (!SystemAPI.TryGetSingletonEntity<PlanetData>(out var planetEntity))
            return;

        var transformLookup = SystemAPI.GetComponentLookup<LocalTransform>(true);
        transformLookup.Update(ref state);

        var selfRotateJob = new SelfRotateJob
        {
            DeltaTime = SystemAPI.Time.DeltaTime,
            PlanetEntity = planetEntity,
            TransformLookup = transformLookup
        };

        var handle = selfRotateJob.ScheduleParallel(state.Dependency);

        state.Dependency = handle;
    }

    [BurstCompile]
    private partial struct SelfRotateJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public Entity PlanetEntity;

        // --- CORRECTION ---
        // Cet attribut dit à Unity d'ignorer le conflit potentiel entre
        // le fait d'écrire sur LocalTransform et de lire ce Lookup.
        [NativeDisableContainerSafetyRestriction]
        [ReadOnly]
        public ComponentLookup<LocalTransform> TransformLookup;

        public void Execute([ChunkIndexInQuery] int chunkIndex, Entity entity, in SelfRotate selfRotate, ref LocalTransform localTransform)
        {
            float3 planetCenter = float3.zero;

            if (TransformLookup.HasComponent(PlanetEntity))
            {
                planetCenter = TransformLookup[PlanetEntity].Position;
            }

            float3 surfaceNormal = math.normalize(localTransform.Position - planetCenter);
            float3 currentForward = localTransform.Forward();
            float3 tangentForward = math.normalize(currentForward - math.dot(currentForward, surfaceNormal) * surfaceNormal);

            quaternion alignedRotation = quaternion.LookRotationSafe(tangentForward, surfaceNormal);
            float angle = math.radians(selfRotate.RotationSpeed) * DeltaTime;

            localTransform.Rotation = math.mul(alignedRotation, quaternion.RotateY(angle));
        }
    }
}