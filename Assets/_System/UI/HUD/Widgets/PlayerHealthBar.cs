using Unity.Entities;
using UnityEngine;

/// <summary>
/// Player health bar on the run HUD. Reads the player's Health/CoreStats from ECS and feeds the
/// shared <see cref="BaseHealthBar"/> view.
/// </summary>
public class PlayerHealthBar : BaseHealthBar
{
    private EntityManager _entityManager;
    private EntityQuery _playerQuery;
    private bool _initialized;

    private void Start()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
            return;

        _entityManager = world.EntityManager;

        _playerQuery = _entityManager.CreateEntityQuery(
            ComponentType.ReadOnly<Player>(),
            ComponentType.ReadOnly<Health>(),
            ComponentType.ReadOnly<CoreStats>()
        );

        _initialized = true;
    }

    protected override void Update()
    {
        if (_initialized && !_playerQuery.IsEmpty)
        {
            // On s'en fou c'est du feedback
            _playerQuery.CompleteDependency();

            var health = _playerQuery.GetSingleton<Health>();
            var stats = _playerQuery.GetSingleton<CoreStats>();

            int maxHealth = Mathf.Max(1, Mathf.FloorToInt(stats.MaxHealth));
            int current = Mathf.Clamp(health.Value, 0, maxHealth);

            SetHealth((float)current / maxHealth);
            SetValue(current, maxHealth);
        }

        base.Update(); // animate the damage trail
    }
}
