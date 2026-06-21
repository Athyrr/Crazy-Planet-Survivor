using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Draws a QuickOutline highlight around lobby buildings while the player is close enough to interact.
/// </summary>
public class LobbyBuildingOutlineController : MonoBehaviour
{
    [Header("Outline look")]
    [SerializeField] private Color _outlineColor = Color.white;
    [SerializeField, Range(0f, 10f)] private float _outlineWidth = 5f;
    [SerializeField] private Outline.Mode _outlineMode = Outline.Mode.OutlineVisible;

    private EntityManager _entityManager;
    private EntityQuery _interactableQuery;
    private bool _initialized;

    private readonly List<ProxyBinding> _proxies = new();
    private Material _invisibleBase;
    private bool _built;

    private struct ProxyBinding
    {
        public Entity InteractableEntity; // root that carries the InteractableInRangeTag
        public GameObject Proxy;
        public Mesh ClonedMesh;
    }

    private void OnDisable() => TeardownProxies();

    private void OnDestroy()
    {
        TeardownProxies();
        if (_invisibleBase != null)
            Destroy(_invisibleBase);
    }

    private void EnsureInit()
    {
        if (_initialized)
            return;

        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;

        _entityManager = world.EntityManager;
        _interactableQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<Interactable>(),
            ComponentType.ReadOnly<LocalToWorld>());

        _initialized = true;
    }

    private void Update()
    {
        // Building outlines only matter while walking the lobby. Outside the lobby (runs, menus, galaxy)
        // the proxies are fully deactivated so they cost nothing to render.
        if (GameManager.Instance == null || GameManager.Instance.GetGameState() != EGameState.Lobby)
        {
            if (_built)
                SetAllProxiesActive(false);
            return;
        }

        EnsureInit();
        if (!_initialized)
            return;

        // The lobby subscene streams in async — wait until the interactable entities exist.
        if (!_built)
        {
            if (_interactableQuery.IsEmpty)
                return;
            BuildProxies();
        }

        // The subscene was reloaded (e.g. after a run): our cached entity handles are stale → rebuild.
        if (_proxies.Count > 0 && !_entityManager.Exists(_proxies[0].InteractableEntity))
        {
            TeardownProxies();
            return;
        }

        // A proxy only exists (and renders its outline) while its building is in interaction range.
        for (int i = 0; i < _proxies.Count; i++)
        {
            var binding = _proxies[i];
            if (binding.Proxy == null)
                continue;

            bool inRange = _entityManager.Exists(binding.InteractableEntity)
                && _entityManager.IsComponentEnabled<InteractableInRangeTag>(binding.InteractableEntity);

            if (binding.Proxy.activeSelf != inRange)
                binding.Proxy.SetActive(inRange);
        }
    }

    private void BuildProxies()
    {
        EnsureInvisibleBase();

        var renderEntities = new List<Entity>();
        // Different buildings expose their visual differently: older ones render on the interactable
        // entity itself; newer ones nest the art under child entities (one render entity per submesh,
        // so several overlap). We walk the whole hierarchy and dedupe by (mesh, world position).
        var seen = new HashSet<(int, long, long, long)>();

        using var entities = _interactableQuery.ToEntityArray(Allocator.Temp);
        for (int i = 0; i < entities.Length; i++)
        {
            var interactableEntity = entities[i];
            var interactable = _entityManager.GetComponentData<Interactable>(interactableEntity);

            renderEntities.Clear();
            seen.Clear();
            CollectRenderEntities(interactableEntity, renderEntities);

            int built = 0;
            foreach (var renderEntity in renderEntities)
            {
                var mesh = TryGetEntityMesh(renderEntity);
                if (mesh == null)
                    continue;

                // QuickOutline computes smooth normals on the CPU, which needs a Read/Write-enabled mesh.
                // Skip (with a clear, actionable warning) rather than letting QuickOutline spam errors.
                if (!mesh.isReadable)
                {
                    Debug.LogWarning($"[LobbyBuildingOutline] Mesh '{mesh.name}' ({interactable.InteractionType}) is not Read/Write enabled — outline skipped. Enable Read/Write in its model import settings.");
                    continue;
                }

                var ltw = _entityManager.GetComponentData<LocalToWorld>(renderEntity);
                var key = (mesh.GetInstanceID(),
                    (long)math.round(ltw.Position.x * 100f),
                    (long)math.round(ltw.Position.y * 100f),
                    (long)math.round(ltw.Position.z * 100f));
                if (!seen.Add(key))
                    continue; // same mesh at same spot → a duplicate submesh entity, already covered

                CreateProxy(interactableEntity, interactable.InteractionType, mesh, ltw, built++);
            }

            if (built == 0)
                Debug.LogWarning($"[LobbyBuildingOutline] No render mesh found under {interactable.InteractionType}; skipping outline.");
        }

        _built = true;
    }

    /// <summary>Spawns one invisible-base + QuickOutline proxy snapped onto a single render entity.</summary>
    private void CreateProxy(Entity interactableEntity, EInteractionType type, Mesh mesh, LocalToWorld ltw, int part)
    {
        // Clone the mesh so QuickOutline's smooth-normal / submesh baking can't touch the shared ECS mesh.
        var meshClone = Instantiate(mesh);
        meshClone.name = mesh.name + " (OutlineProxy)";

        var proxy = new GameObject($"OutlineProxy_{type}_{part}");
        var m = ltw.Value;
        proxy.transform.SetPositionAndRotation(ltw.Position, ltw.Rotation);
        proxy.transform.localScale = new Vector3(math.length(m.c0.xyz), math.length(m.c1.xyz), math.length(m.c2.xyz));

        var meshFilter = proxy.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = meshClone;

        var meshRenderer = proxy.AddComponent<MeshRenderer>();
        int submeshes = Mathf.Max(1, meshClone.subMeshCount);
        var mats = new Material[submeshes];
        for (int s = 0; s < submeshes; s++)
            mats[s] = _invisibleBase;
        meshRenderer.sharedMaterials = mats;
        meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        var outline = proxy.AddComponent<Outline>();
        outline.OutlineMode = _outlineMode;
        outline.OutlineColor = _outlineColor;
        outline.OutlineWidth = _outlineWidth;

        // The proxy carries an always-on outline; we gate visibility by (de)activating the whole
        // GameObject so an out-of-range proxy renders nothing at all.
        proxy.SetActive(false);

        _proxies.Add(new ProxyBinding { InteractableEntity = interactableEntity, Proxy = proxy, ClonedMesh = meshClone });
    }

    /// <summary>Collects <paramref name="entity"/> and every descendant that renders a mesh.</summary>
    private void CollectRenderEntities(Entity entity, List<Entity> output)
    {
        if (_entityManager.HasComponent<MaterialMeshInfo>(entity)
            && _entityManager.HasComponent<RenderMeshArray>(entity))
            output.Add(entity);

        if (!_entityManager.HasComponent<Child>(entity))
            return;

        // Snapshot the children before recursing (defensive — we never make structural changes here).
        var childBuffer = _entityManager.GetBuffer<Child>(entity);
        int count = childBuffer.Length;
        if (count == 0)
            return;
        var children = new NativeArray<Entity>(count, Allocator.Temp);
        for (int i = 0; i < count; i++)
            children[i] = childBuffer[i].Value;
        for (int i = 0; i < count; i++)
            CollectRenderEntities(children[i], output);
        children.Dispose();
    }

    /// <summary>Reads the actual mesh an Entities-Graphics entity renders, or null if it has none.</summary>
    private Mesh TryGetEntityMesh(Entity entity)
    {
        if (!_entityManager.HasComponent<MaterialMeshInfo>(entity)
            || !_entityManager.HasComponent<RenderMeshArray>(entity))
            return null;

        var materialMeshInfo = _entityManager.GetComponentData<MaterialMeshInfo>(entity);
        var renderMeshArray = _entityManager.GetSharedComponentManaged<RenderMeshArray>(entity);
        return renderMeshArray.GetMesh(materialMeshInfo);
    }

    private void EnsureInvisibleBase()
    {
        if (_invisibleBase != null)
            return;

        var shader = Shader.Find("Hidden/QuickOutline/InvisibleBase");
        if (shader != null)
            _invisibleBase = new Material(shader) { name = "OutlineInvisibleBase (Instance)" };
        else
            Debug.LogWarning("[LobbyBuildingOutline] Invisible base shader missing — proxies will re-draw the building.");
    }

    private void SetAllProxiesActive(bool active)
    {
        for (int i = 0; i < _proxies.Count; i++)
        {
            if (_proxies[i].Proxy != null && _proxies[i].Proxy.activeSelf != active)
                _proxies[i].Proxy.SetActive(active);
        }
    }

    private void TeardownProxies()
    {
        for (int i = 0; i < _proxies.Count; i++)
        {
            if (_proxies[i].Proxy != null)
                Destroy(_proxies[i].Proxy);
            if (_proxies[i].ClonedMesh != null)
                Destroy(_proxies[i].ClonedMesh);
        }

        _proxies.Clear();
        _built = false;
    }
}
