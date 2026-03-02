using Unity.Burst;
using Unity.Entities;
using Unity.Rendering;
using Unity.Collections;

[UpdateInGroup(typeof(PresentationSystemGroup))]
[BurstCompile]
public partial struct HitFrameFeedbackSystem : ISystem
{
    private const string MaterialPropName = "_PowerHit";
    private const float MaterialPropMin = 0;
    private const float MaterialPropMax = 5f;
    private const float FlashDuration = 0.15f;

    [MaterialProperty(MaterialPropName)]
    public struct HitFrameColor : IComponentData, IEnableableComponent
    {
        public float Value;
    }

    public struct HitFrameColorRequest : IComponentData
    {
        public Entity TargetEntity;
    }

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<BeginPresentationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<BeginPresentationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var entityManager = state.EntityManager;
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (request, requestEntity) in SystemAPI.Query<RefRO<HitFrameColorRequest>>().WithEntityAccess())
        {
            Entity target = request.ValueRO.TargetEntity;

            if (entityManager.Exists(target))
            {
                if (entityManager.HasBuffer<LinkedEntityGroup>(target))
                {
                    var children = entityManager.GetBuffer<LinkedEntityGroup>(target);
                    foreach (var child in children)
                    {
                        if (entityManager.HasComponent<HitFrameColor>(child.Value))
                        {
                            ApplyFlashImmediate(entityManager, child.Value);
                        }
                    }
                }
                else if (entityManager.HasComponent<HitFrameColor>(target))
                {
                    ApplyFlashImmediate(entityManager, target);
                }
            }

            ecb.DestroyEntity(requestEntity);
        }

        var toDisable = new NativeList<Entity>(Allocator.Temp);

        foreach (var (hitColor, entity) in SystemAPI.Query<RefRW<HitFrameColor>>().WithEntityAccess())
        {
            float decreasing = (MaterialPropMax / FlashDuration) * deltaTime;
            hitColor.ValueRW.Value -= decreasing;

            if (hitColor.ValueRW.Value <= MaterialPropMin)
            {
                hitColor.ValueRW.Value = MaterialPropMin;
                toDisable.Add(entity);
            }
        }

        foreach (var entity in toDisable)
        {
            if (entityManager.Exists(entity))
            {
                entityManager.SetComponentEnabled<HitFrameColor>(entity, false);
            }
        }

        toDisable.Dispose();
    }

    private static void ApplyFlashImmediate(EntityManager em, Entity target)
    {
        em.SetComponentEnabled<HitFrameColor>(target, true);
        em.SetComponentData(target, new HitFrameColor { Value = MaterialPropMax });
    }
}