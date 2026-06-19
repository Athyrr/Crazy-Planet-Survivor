using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Spawns/follows/despawns the per-entity status-effect VFX (burn, slow, stun) for every entity whose
/// effect component is currently <b>enabled</b>.
/// </summary>
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class VfxPresentationSystem : SystemBase
{
    /// <summary>Entity render-bounds diameter at which a prefab (scale 1) is sized correctly. Tune to taste.</summary>
    private const float ReferenceDiameter = 2f;

    private EntityQuery _burnQuery, _slowQuery, _stunQuery;

    private readonly Dictionary<Entity, GameObject> _burnVfx = new();
    private readonly Dictionary<Entity, GameObject> _slowVfx = new();
    private readonly Dictionary<Entity, GameObject> _stunVfx = new();

    private readonly HashSet<Entity> _enabledScratch = new();
    private readonly List<Entity> _removeScratch = new();

    protected override void OnCreate()
    {
        RequireForUpdate<ActiveEffectsVfxConfig>();

        _burnQuery = BuildQuery<BurnEffect>();
        _slowQuery = BuildQuery<SlowEffect>();
        _stunQuery = BuildQuery<StunEffect>();
    }

    protected override void OnDestroy()
    {
        DestroyAll(_burnVfx);
        DestroyAll(_slowVfx);
        DestroyAll(_stunVfx);
    }

    private EntityQuery BuildQuery<TEffect>() where TEffect : unmanaged, IComponentData, IEnableableComponent =>
        new EntityQueryBuilder(Allocator.Temp).WithAll<TEffect, LocalToWorld>().Build(EntityManager);

    protected override void OnUpdate()
    {
        var config = SystemAPI.ManagedAPI.GetSingleton<ActiveEffectsVfxConfig>();

        SyncEffectVfx(config.BurnEffectPrefab, _burnVfx, _burnQuery);
        SyncEffectVfx(config.SlowEffectPrefab, _slowVfx, _slowQuery);
        SyncEffectVfx(config.StunEffectPrefab, _stunVfx, _stunQuery);
    }

    private void SyncEffectVfx(GameObject prefab, Dictionary<Entity, GameObject> active, EntityQuery enabledQuery)
    {
        using var entities = enabledQuery.ToEntityArray(Allocator.Temp);

        _enabledScratch.Clear();

        // Spawn (if needed) + place/scale every entity whose effect is currently enabled.
        foreach (var entity in entities)
        {
            _enabledScratch.Add(entity);

            if (!active.TryGetValue(entity, out var vfx) || vfx == null)
            {
                if (prefab == null)
                    continue;
                vfx = Object.Instantiate(prefab);
                SetupVfxInstance(vfx);
                active[entity] = vfx;
            }

            var ltw = EntityManager.GetComponentData<LocalToWorld>(entity);
            float3 position = ltw.Position;

            if (TryGetRenderBounds(entity, out float3 center, out float diameter) && diameter > 0f)
            {
                position = center;
                vfx.transform.localScale = prefab.transform.localScale * (diameter / ReferenceDiameter);
            }

            vfx.transform.SetPositionAndRotation(position, ltw.Rotation);
        }

        // Despawn: tracked VFX whose entity no longer has the effect enabled (or was destroyed).
        _removeScratch.Clear();
        foreach (var kv in active)
            if (!_enabledScratch.Contains(kv.Key))
                _removeScratch.Add(kv.Key);

        foreach (var entity in _removeScratch)
        {
            if (active[entity] != null)
                Object.Destroy(active[entity]);
            active.Remove(entity);
        }
    }

    /// <summary>
    /// World render-bounds centre + diameter of the entity, looking through a <see cref="LinkedEntityGroup"/>
    /// (the renderer is often a child entity). Returns false when nothing renderable is found.
    /// </summary>
    private bool TryGetRenderBounds(Entity entity, out float3 center, out float diameter)
    {
        if (TryBounds(entity, out center, out diameter))
            return true;

        if (EntityManager.HasComponent<LinkedEntityGroup>(entity))
        {
            var children = EntityManager.GetBuffer<LinkedEntityGroup>(entity);
            bool any = false;
            for (int i = 0; i < children.Length; i++)
            {
                if (TryBounds(children[i].Value, out float3 c, out float d) && d > diameter)
                {
                    center = c;
                    diameter = d;
                    any = true;
                }
            }
            return any;
        }

        return false;
    }

    private bool TryBounds(Entity entity, out float3 center, out float diameter)
    {
        if (EntityManager.HasComponent<WorldRenderBounds>(entity))
        {
            var aabb = EntityManager.GetComponentData<WorldRenderBounds>(entity).Value;
            center = aabb.Center;
            diameter = math.cmax(aabb.Extents) * 2f;
            return true;
        }

        center = default;
        diameter = 0f;
        return false;
    }

    /// <summary>
    /// Prepares a freshly instantiated VFX so it works without a target mesh: forces particle systems to
    /// scale with the transform (so localScale resizes the whole effect) and rewrites mesh-surface
    /// emitters — which can't work on ECS-rendered entities ("zero surface area") — into a hemisphere
    /// volume sized by the transform.
    /// </summary>
    private static void SetupVfxInstance(GameObject vfx)
    {
        var systems = vfx.GetComponentsInChildren<ParticleSystem>(true);
        foreach (var ps in systems)
        {
            var main = ps.main;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;

            var shape = ps.shape;
            if (shape.enabled && IsMeshShape(shape.shapeType))
            {
                shape.shapeType = ParticleSystemShapeType.Hemisphere;
                shape.radius = 1f; // unit volume; the transform localScale grows it to the entity size
            }
        }
    }

    private static bool IsMeshShape(ParticleSystemShapeType type) =>
        type == ParticleSystemShapeType.Mesh
        || type == ParticleSystemShapeType.MeshRenderer
        || type == ParticleSystemShapeType.SkinnedMeshRenderer;

    private static void DestroyAll(Dictionary<Entity, GameObject> active)
    {
        foreach (var kv in active)
            if (kv.Value != null)
                Object.Destroy(kv.Value);
        active.Clear();
    }
}

/// <summary>
/// Link to a GameObject renderer for an entity. Baked by some authorings (e.g. enemies); kept for
/// compatibility. Not used by the effect-VFX system (entities are ECS-rendered).
/// </summary>
public class VisualRendererLink : IComponentData
{
    public Renderer Renderer;
}
