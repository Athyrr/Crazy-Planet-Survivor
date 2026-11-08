using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Read-only bridge ECS → BeamVisual (spec channeled-beam): one GameObject per ray, created on first sight,
/// fed every frame with the raised chord (AreaSegment A/B + OriginHeight along each end's normal) and the hit width
/// (RadiusStart / PrefabRadius). Ray off or caster invalid → Hide; entity gone (Entity compared with its Version) →
/// GameObject destroyed. No gameplay reads the GameObject. Hybrid presentation for few instances (heuristics) — not
/// the VfxPresentationSystem workaround.</summary>
[UpdateInGroup(typeof(PresentationSystemGroup))]
public partial class BeamVisualPresentationSystem : SystemBase
{
    private EntityQuery _rayQuery;
    private readonly Dictionary<Entity, BeamVisual> _visuals = new Dictionary<Entity, BeamVisual>();
    private readonly HashSet<Entity> _broken = new HashSet<Entity>();
    private readonly HashSet<Entity> _seen = new HashSet<Entity>();
    private readonly List<Entity> _remove = new List<Entity>();

    protected override void OnCreate()
    {
        _rayQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<BeamAim, BeamVisualPrefab, AreaSegment, AreaAttack, EmitterAnchor>()
            .WithPresent<BeamActive>() // off rays too, or Hide would never be called
            .Build(this);
        // No RequireForUpdate: the system must still run after the last ray is gone, to destroy its GameObject.
    }

    protected override void OnDestroy()
    {
        foreach (var kv in _visuals)
            if (kv.Value != null)
                Object.Destroy(kv.Value.gameObject);
        _visuals.Clear();
    }

    protected override void OnUpdate()
    {
        if (_visuals.Count == 0 && _rayQuery.IsEmptyIgnoreFilter)
            return;

        CompleteDependency();
        using var rays = _rayQuery.ToEntityArray(Allocator.Temp);
        _seen.Clear();

        foreach (var ray in rays)
        {
            _seen.Add(ray);
            if (_broken.Contains(ray))
                continue;

            if (!_visuals.TryGetValue(ray, out var visual) || visual == null)
            {
                GameObject prefab = EntityManager.GetComponentData<BeamVisualPrefab>(ray).Value.Value;
                if (prefab == null)
                {
                    Debug.LogError($"[BeamVisualPresentationSystem] ray {ray} has no resolvable VisualPrefab (UnityObjectRef).");
                    _broken.Add(ray);
                    continue;
                }
                var go = Object.Instantiate(prefab);
                visual = go.GetComponent<BeamVisual>();
                if (visual == null)
                {
                    Debug.LogError($"[BeamVisualPresentationSystem] '{prefab.name}' has no BeamVisual at its root.", prefab);
                    Object.Destroy(go);
                    _broken.Add(ray);
                    continue;
                }
                // BeamVisual caches its state in Awake: an inactive root would make Show/SetEndpoints throw.
                if (!go.activeSelf)
                    go.SetActive(true);
                _visuals[ray] = visual;
            }

            if (!EntityManager.IsComponentEnabled<BeamActive>(ray) || !CasterValid(ray))
            {
                visual.Hide();
                continue;
            }

            var segment = EntityManager.GetComponentData<AreaSegment>(ray);
            var area = EntityManager.GetComponentData<AreaAttack>(ray);
            float height = EntityManager.GetComponentData<BeamAim>(ray).OriginHeight;
            float3 up = new float3(0f, 1f, 0f);
            float3 nA = math.normalizesafe(segment.A - segment.Center, up);
            float3 nB = math.normalizesafe(segment.B - segment.Center, up);

            visual.SetEndpoints(segment.A + nA * height, segment.B + nB * height);
            visual.SetWidth(area.PrefabRadius > 0f ? area.RadiusStart / area.PrefabRadius : 1f);
            visual.Show();
        }

        _remove.Clear();
        foreach (var kv in _visuals)
            if (!_seen.Contains(kv.Key))
                _remove.Add(kv.Key);
        foreach (var entity in _remove)
        {
            if (_visuals[entity] != null)
                Object.Destroy(_visuals[entity].gameObject);
            _visuals.Remove(entity);
        }
        _broken.RemoveWhere(e => !_seen.Contains(e));
    }

    private bool CasterValid(Entity ray)
    {
        Entity owner = EntityManager.GetComponentData<EmitterAnchor>(ray).Owner;
        if (!EntityManager.Exists(owner) || !EntityManager.HasComponent<SpellSource>(owner))
            return false;
        Entity caster = EntityManager.GetComponentData<SpellSource>(owner).CasterEntity;
        return EntityManager.Exists(caster) && EntityManager.HasComponent<LocalTransform>(caster);
    }
}
