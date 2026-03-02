using Unity.Burst;
using Unity.Entities;
using Unity.Rendering;
using Unity.Collections;

[UpdateInGroup(typeof(PresentationSystemGroup))]
[BurstCompile]
public partial struct HitFrameFreedbackSystem : ISystem
{
    private const string MaterialPropName = "_PowerHit";
    private const float MATERIAL_PROP_MIN = 0;
    private const float MATERIAL_PROP_MAX = 2f;
    private const float FLASH_DURATION = 1.0f; // Duration of the flash effect in seconds

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

        float deltaTime = SystemAPI.Time.DeltaTime;

        // 1. Gestion des requêtes (Nouvel impact)
        foreach (var (request, requestEntity) in SystemAPI.Query<RefRO<HitFrameColorRequest>>().WithEntityAccess())
        {
            Entity target = request.ValueRO.TargetEntity;

            if (state.EntityManager.Exists(target))
            {
                if (state.EntityManager.HasBuffer<LinkedEntityGroup>(target))
                {
                    var children = state.EntityManager.GetBuffer<LinkedEntityGroup>(target);
                    foreach (var child in children)
                    {
                        if (state.EntityManager.HasComponent<MaterialMeshInfo>(child.Value) &&
                            state.EntityManager.HasComponent<HitFrameColor>(child.Value))
                        {
                            ApplyFlash(ecb, child.Value);
                        }
                    }
                }

                else if (state.EntityManager.HasComponent<MaterialMeshInfo>(target) &&
                         state.EntityManager.HasComponent<HitFrameColor>(target))
                {
                    ApplyFlash(ecb, target);
                }
            }

            ecb.DestroyEntity(requestEntity);
        }

        foreach (var (hitColor, entity) in SystemAPI.Query<RefRW<HitFrameColor>>().WithEntityAccess())
        {
            float decreasing = (MATERIAL_PROP_MAX / FLASH_DURATION) * deltaTime;
            hitColor.ValueRW.Value -= decreasing;

            if (hitColor.ValueRW.Value <= MATERIAL_PROP_MIN)
            {
                hitColor.ValueRW.Value = MATERIAL_PROP_MIN;
                ecb.SetComponentEnabled<HitFrameColor>(entity, false);
            }
        }
    }

    private static void ApplyFlash(EntityCommandBuffer ecb, Entity target)
    {
        ecb.SetComponentEnabled<HitFrameColor>(target, true);
        ecb.SetComponent(target, new HitFrameColor { Value = MATERIAL_PROP_MAX });
    }
}