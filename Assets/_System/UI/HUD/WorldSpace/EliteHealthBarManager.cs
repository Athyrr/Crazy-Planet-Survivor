using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Drives pooled elite health bars on the 2D run canvas, each positioned from its elite's 3D world
/// position (an entity carrying <see cref="EliteTag"/>).
/// Elite's world position is projected to screen space.
/// </summary>
public class EliteHealthBarManager : MonoBehaviour
{
    [Header("Bar")]
    [Tooltip("Screen-space health-bar prefab (Slider) instantiated into the pool.")]
    public EliteHealthBar BarPrefab;

    [Tooltip("Parent for the spawned bars. Must be a RectTransform under the 2D run canvas.")]
    public RectTransform Container;

    [Tooltip("Number of bars pre-instantiated on start.")]
    public int InitialPoolSize = 8;

    [Header("Placement")]
    [Tooltip("World-space height above the elite (along the camera's up axis) before projecting to screen.")]
    public float HeightOffset = 0.8f;

    [Tooltip("Multiply the height offset by the entity's uniform scale (elites are scaled up).")]
    public bool ScaleOffsetByEntity = true;

    [Header("Camera")]
    [Tooltip("Gameplay (3D) camera used to project world positions to screen. Defaults to Camera.main.")]
    public Camera TargetCamera;

    private EntityManager _entityManager;
    private EntityQuery _eliteQuery;
    private bool _initialized;

    private Canvas _canvas;
    private Camera _uiCamera; // camera passed to RectTransformUtility (null for ScreenSpaceOverlay)

    private readonly Dictionary<Entity, EliteHealthBar> _active = new();
    private readonly Stack<EliteHealthBar> _pool = new();
    private readonly List<Entity> _toRelease = new();

    private void Start()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
            return;

        if (Container == null)
        {
            Debug.LogError("[EliteHealthBarManager] Container is not assigned (must be a RectTransform under the 2D run canvas).", this);
            return;
        }

        _entityManager = world.EntityManager;

        _eliteQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<EliteTag>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<CoreStats>(),
            ComponentType.ReadOnly<LocalToWorld>()
        );

        _canvas = Container.GetComponentInParent<Canvas>();
        _uiCamera = (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? _canvas.worldCamera
            : null;

        for (int i = 0; i < InitialPoolSize; i++)
            _pool.Push(CreateBar());

        _initialized = true;
    }

    private void LateUpdate()
    {
        if (!_initialized || BarPrefab == null || Container == null)
            return;

        var cam = GetCamera();
        if (cam == null)
            return;

        // Complete jobs before reading transforms/health (feedback only, like the other HUD widgets).
        _eliteQuery.CompleteDependency();

        var entities = _eliteQuery.ToEntityArray(Allocator.Temp);

        for (int i = 0; i < entities.Length; i++)
        {
            var entity = entities[i];

            bool justBound = false;
            if (!_active.TryGetValue(entity, out var bar))
            {
                bar = GetBar();
                _active[entity] = bar;
                justBound = true;

                string displayName = null;
                if (_entityManager.HasComponent<BossPresentation>(entity))
                {
                    var presentation = _entityManager.GetComponentObject<BossPresentation>(entity);
                    displayName = presentation != null ? presentation.DisplayName : null;
                }
                bar.SetName(displayName);
            }

            var ltw = _entityManager.GetComponentData<LocalToWorld>(entity);
            var health = _entityManager.GetComponentData<Health>(entity);
            var stats = _entityManager.GetComponentData<CoreStats>(entity);

            int maxHealth = Mathf.Max(1, Mathf.FloorToInt(stats.MaxHealth));
            int current = Mathf.Clamp(health.Value, 0, maxHealth);

            float scale = ScaleOffsetByEntity ? math.length(ltw.Value.c0.xyz) : 1f;

            // Raise the anchor a bit above the entity (along the camera's up), then project to screen.
            Vector3 worldPos = (Vector3)ltw.Position + cam.transform.up * (HeightOffset * scale);
            Vector3 screenPoint = cam.WorldToScreenPoint(worldPos);

            float ratio = (float)current / maxHealth;
            if (justBound)
                bar.ResetTo(ratio); // snap a freshly-bound (pooled) bar, no drain animation
            else
                bar.SetHealth(ratio);

            // Hide the bar when the elite is behind the camera.
            bool visible = screenPoint.z > 0f;
            if (bar.gameObject.activeSelf != visible)
                bar.gameObject.SetActive(visible);

            if (visible)
                PlaceBar(bar, screenPoint);
        }

        // Recycle bars whose elite no longer exists.
        _toRelease.Clear();
        foreach (var kvp in _active)
        {
            if (!ContainsEntity(entities, kvp.Key))
                _toRelease.Add(kvp.Key);
        }

        for (int i = 0; i < _toRelease.Count; i++)
        {
            var key = _toRelease[i];
            ReleaseBar(_active[key]);
            _active.Remove(key);
        }

        entities.Dispose();
    }

    private void PlaceBar(EliteHealthBar bar, Vector3 screenPoint)
    {
        var rt = (RectTransform)bar.transform;

        if (_canvas == null || _canvas.renderMode == RenderMode.ScreenSpaceOverlay)
        {
            // For an overlay canvas, RectTransform.position is in screen pixels.
            rt.position = new Vector3(screenPoint.x, screenPoint.y, 0f);
            return;
        }

        if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                Container, new Vector2(screenPoint.x, screenPoint.y), _uiCamera, out var local))
            rt.anchoredPosition = local;
    }

    private static bool ContainsEntity(NativeArray<Entity> entities, Entity target)
    {
        for (int i = 0; i < entities.Length; i++)
            if (entities[i] == target)
                return true;
        return false;
    }

    private Camera GetCamera()
    {
        // Re-resolve if the cached camera was destroyed or disabled (e.g. the menu camera is
        // swapped for the gameplay camera when a run starts).
        if (TargetCamera != null && TargetCamera.isActiveAndEnabled)
            return TargetCamera;

        var main = Camera.main;
        if (main != null)
        {
            TargetCamera = main;
            return main;
        }

        // Fallback: pick the highest-depth enabled camera that isn't an obvious UI overlay.
        var cams = Camera.allCameras; // returns enabled cameras only
        Camera best = null;
        for (int i = 0; i < cams.Length; i++)
        {
            var c = cams[i];
            if (c == null) continue;
            if (c.name.IndexOf("UI", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
            if (best == null || c.depth > best.depth) best = c;
        }
        if (best == null && cams.Length > 0) best = cams[0];

        TargetCamera = best;
        return best;
    }

    private EliteHealthBar CreateBar()
    {
        var bar = Instantiate(BarPrefab, Container, false);
        bar.gameObject.SetActive(false);
        return bar;
    }

    private EliteHealthBar GetBar()
    {
        var bar = _pool.Count > 0 ? _pool.Pop() : CreateBar();
        bar.gameObject.SetActive(true);
        return bar;
    }

    private void ReleaseBar(EliteHealthBar bar)
    {
        bar.gameObject.SetActive(false);
        _pool.Push(bar);
    }
}
