using Unity.Collections;
using Unity.Entities;
using UnityEngine;

/// <summary>
/// Drives the 2D boss health bar at the top of the run HUD. Reads the active final boss from ECS and
/// feeds the shared <see cref="BaseHealthBar"/> view (damage trail + name + numeric label).
/// The bar shows itself only while a <see cref="FinalBossTag"/> entity is alive.
/// </summary>
public class BossHealthBar : BaseHealthBar
{
    [Header("Boss bar")]
    [Tooltip("Visual root toggled on/off based on whether a final boss is alive. Should be a child object, not this one.")]
    public GameObject BarRoot;

    private EntityManager _entityManager;
    private EntityQuery _bossQuery;
    private bool _initialized;
    private bool _isShown;

    private void Start()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
            return;

        _entityManager = world.EntityManager;

        _bossQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<FinalBossTag>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<CoreStats>(),
            ComponentType.ReadOnly<BossPresentation>()
        );

        _initialized = true;
        SetVisible(false);
    }

    protected override void Update()
    {
        if (!_initialized)
            return;

        if (_bossQuery.IsEmpty)
        {
            if (_isShown)
                SetVisible(false);
            return;
        }

        // Complete all jobs before refreshing visual
        _bossQuery.CompleteDependency();

        var entities = _bossQuery.ToEntityArray(Allocator.Temp);
        var bossEntity = entities[0];
        entities.Dispose();

        var health = _entityManager.GetComponentData<Health>(bossEntity);
        var stats = _entityManager.GetComponentData<CoreStats>(bossEntity);

        int maxHealth = Mathf.Max(1, Mathf.FloorToInt(stats.MaxHealth));
        int current = Mathf.Clamp(health.Value, 0, maxHealth);
        float ratio = (float)current / maxHealth;

        if (!_isShown)
        {
            SetVisible(true);

            var presentation = _entityManager.GetComponentObject<BossPresentation>(bossEntity);
            SetName(presentation != null ? presentation.DisplayName : string.Empty);
            ResetTo(ratio); // snap on first appearance, no trail from full
        }
        else
        {
            SetHealth(ratio);
        }

        SetValue(current, maxHealth);
        base.Update(); // animate the damage trail
    }

    private void SetVisible(bool visible)
    {
        _isShown = visible;

        if (BarRoot != null)
            BarRoot.SetActive(visible);
    }
}
