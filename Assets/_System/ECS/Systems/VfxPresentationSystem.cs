using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Object = UnityEngine.Object;

[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class VfxPresentationSystem : SystemBase
{
    private GameObject BurnVfxPrefab;
    private GameObject StunVfxPrefab;
    private GameObject SlowVfxPrefab;

    protected override void OnCreate()
    {
        RequireForUpdate<ActiveEffectsVfxConfig>();
    }

    protected override void OnUpdate()
    {
        // var config = SystemAPI.ManagedAPI.GetSingleton<ActiveEffectsVfxConfig>();
        //
        // BurnVfxPrefab = config.BurnEffectPrefab;
        // StunVfxPrefab = config.StunEffectPrefab;
        // SlowVfxPrefab = config.SlowEffectPrefab;
        //
        // var commandBuffer = new EntityCommandBuffer(Allocator.Temp);
        //
        // //Burn
        // if (BurnVfxPrefab != null)
        // {
        //     Entities
        //         .WithAll<BurnEffect>()
        //         .WithNone<BurnVisualEffectLink>()
        //         .WithoutBurst()
        //         .ForEach((Entity entity, VisualRendererLink rendererLink) =>
        //         {
        //             GameObject burnVisual = Object.Instantiate(BurnVfxPrefab, rendererLink.Renderer.transform, false);
        //             burnVisual.transform.localPosition = Vector3.zero;
        //
        //             if (burnVisual.TryGetComponent<OverlayFX>(out var overlay))
        //                 overlay.targetRenderer = rendererLink.Renderer;
        //
        //             EntityManager.AddComponentData(entity, new BurnVisualEffectLink { effectObject = burnVisual });
        //         }).Run();
        //
        //     Entities
        //         .WithNone<BurnEffect>() 
        //         .WithAll<BurnVisualEffectLink>()
        //         .WithoutBurst()
        //         .ForEach((Entity entity, BurnVisualEffectLink visualEffectLink) =>
        //         {
        //             visualEffectLink.Dispose();
        //             commandBuffer.RemoveComponent<BurnVisualEffectLink>(entity);
        //         }).Run();
        // }
        //
        // // Slow
        // if (SlowVfxPrefab != null)
        // {
        //     Entities
        //         .WithAll<SlowEffect>()
        //         .WithNone<SlowVfxLink>()
        //         .WithoutBurst()
        //         .ForEach((Entity entity, VisualRendererLink rendererLink) =>
        //         {
        //             GameObject vfx = Object.Instantiate(SlowVfxPrefab, rendererLink.Renderer.transform, false);
        //             vfx.transform.localPosition = Vector3.zero;
        //
        //             if (vfx.TryGetComponent<OverlayFX>(out var overlay))
        //                 overlay.targetRenderer = rendererLink.Renderer;
        //
        //             EntityManager.AddComponentData(entity, new SlowVfxLink { VfxInstance = vfx });
        //         }).Run();
        //
        //     Entities
        //         .WithNone<SlowEffect>()
        //         .WithAll<SlowVfxLink>()
        //         .WithoutBurst()
        //         .ForEach((Entity entity, SlowVfxLink vfxLink) =>
        //         {
        //             vfxLink.Dispose();
        //             commandBuffer.RemoveComponent<SlowVfxLink>(entity);
        //         }).Run();
        // }
        //
        // commandBuffer.Playback(EntityManager);
        // commandBuffer.Dispose();
    }
}

public class BurnVisualEffectLink : IComponentData, IDisposable
{
    public GameObject effectObject;

    public void Dispose()
    {
        if (effectObject != null) GameObject.Destroy(effectObject);
    }
}

public class SlowVfxLink : IComponentData, IDisposable
{
    public GameObject VfxInstance;

    public void Dispose()
    {
        if (VfxInstance != null) GameObject.Destroy(VfxInstance);
    }
}

public class StunVfxLink : IComponentData, IDisposable
{
    public GameObject VfxInstance;

    public void Dispose()
    {
        if (VfxInstance != null) GameObject.Destroy(VfxInstance);
    }
}

/// <summary>
/// Link the renderer component to an entity
/// </summary>
public class VisualRendererLink : IComponentData
{
    public Renderer Renderer;
}