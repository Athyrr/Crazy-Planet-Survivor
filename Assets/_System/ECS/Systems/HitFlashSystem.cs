using Unity.Collections;
using Unity.Entities;

/// <summary>
/// Hit flash: consumes HitFlashRequest, lights HitFlash on the target (or on its LinkedEntityGroup members that
/// carry it), then decays the value to 0 over FlashDuration and disables the component.
/// </summary>
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class HitFlashSystem : SystemBase
{
    private const float FlashMin = 0f;
    private const float FlashMax = 5f;
    private const float FlashDuration = 0.15f;

    private static void ApplyFlashImmediate(EntityManager em, Entity target)
    {
        em.SetComponentEnabled<HitFlash>(target, true);
        em.SetComponentData(target, new HitFlash { Value = FlashMax });
    }

    protected override void OnUpdate()
    {
        var ecbSingleton = SystemAPI.GetSingleton<BeginPresentationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(EntityManager.WorldUnmanaged);

        var entityManager = EntityManager;
        float deltaTime = SystemAPI.Time.DeltaTime;

        foreach (var (request, requestEntity) in SystemAPI.Query<RefRO<HitFlashRequest>>().WithEntityAccess())
        {
            Entity target = request.ValueRO.TargetEntity;

            if (entityManager.Exists(target))
            {
                if (entityManager.HasBuffer<LinkedEntityGroup>(target))
                {
                    var children = entityManager.GetBuffer<LinkedEntityGroup>(target);
                    foreach (var child in children)
                    {
                        if (entityManager.HasComponent<HitFlash>(child.Value))
                            ApplyFlashImmediate(entityManager, child.Value);
                    }
                }
                else if (entityManager.HasComponent<HitFlash>(target))
                {
                    ApplyFlashImmediate(entityManager, target);
                }
            }

            ecb.DestroyEntity(requestEntity);
        }

        var toDisable = new NativeList<Entity>(Allocator.Temp);
        float decreasing = (FlashMax / FlashDuration) * deltaTime;

        foreach (var (flash, entity) in SystemAPI.Query<RefRW<HitFlash>>().WithEntityAccess())
        {
            flash.ValueRW.Value -= decreasing;

            if (flash.ValueRW.Value <= FlashMin)
            {
                flash.ValueRW.Value = FlashMin;
                toDisable.Add(entity);
            }
        }

        foreach (var entity in toDisable)
            entityManager.SetComponentEnabled<HitFlash>(entity, false);

        toDisable.Dispose();
    }
}
