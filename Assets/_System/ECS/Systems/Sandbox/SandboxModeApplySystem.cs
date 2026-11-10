#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Collections;
using Unity.Entities;

/// <summary>Applies SandboxSettings to the world. Bootstrap: on the Sandbox planet, creates (or resets a
/// command-created) singleton with Sandbox defaults, RunScope so it dies with the run. Modes: batched structural changes
/// on present and future enemies (dummies excluded, they bake their own floor and Frozen) and on the player, only when
/// something is new; on→off transitions remove. Drains the player's caught lethal blows into PlayerDeaths.
/// Dev-only, not Burst (Debug.Log, generic helper).</summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(PlayerSpawnerSystem))]
public partial struct SandboxModeApplySystem : ISystem
{
    private EntityQuery _settingsQuery;
    private EntityQuery _enemiesToFreeze, _enemiesFrozen;
    private EntityQuery _enemiesToProtect, _enemiesProtected;
    private EntityQuery _enemiesToFloor, _enemiesFloored;
    private EntityQuery _playerToFloor;
    private EntityQuery _playerToGod, _playerGod;
    private EntityQuery _playerToFreeze, _playerFrozen;

    private bool _lastFrozen, _lastInvincible, _lastImmortal, _lastGod, _lastPlayerFrozen;
    private float _lastRegenDelay;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameState>();
        state.RequireForUpdate<PlanetData>();

        _settingsQuery = state.GetEntityQuery(ComponentType.ReadWrite<SandboxSettings>());

        _enemiesToFreeze = new EntityQueryBuilder(Allocator.Temp).WithAll<Enemy>().WithNone<Frozen, DummyTag>().Build(ref state);
        _enemiesFrozen = new EntityQueryBuilder(Allocator.Temp).WithAll<Enemy, Frozen>().WithNone<DummyTag>().Build(ref state);
        _enemiesToProtect = new EntityQueryBuilder(Allocator.Temp).WithAll<Enemy>().WithNone<Invincible, DummyTag>().Build(ref state);
        _enemiesProtected = new EntityQueryBuilder(Allocator.Temp).WithAll<Enemy, Invincible>().WithNone<DummyTag>().Build(ref state);
        _enemiesToFloor = new EntityQueryBuilder(Allocator.Temp).WithAll<Enemy>().WithNone<HealthFloor, DummyTag>().Build(ref state);
        _enemiesFloored = new EntityQueryBuilder(Allocator.Temp).WithAll<Enemy, HealthFloor>().WithNone<DummyTag>().Build(ref state);

        _playerToFloor = new EntityQueryBuilder(Allocator.Temp).WithAll<Player>().WithNone<HealthFloor>().Build(ref state);
        _playerToGod = new EntityQueryBuilder(Allocator.Temp).WithAll<Player>().WithNone<Invincible>().Build(ref state);
        _playerGod = new EntityQueryBuilder(Allocator.Temp).WithAll<Player, Invincible>().Build(ref state);
        _playerToFreeze = new EntityQueryBuilder(Allocator.Temp).WithAll<Player>().WithNone<Frozen>().Build(ref state);
        _playerFrozen = new EntityQueryBuilder(Allocator.Temp).WithAll<Player, Frozen>().Build(ref state);
    }

    public void OnUpdate(ref SystemState state)
    {
        var em = state.EntityManager;
        var gameState = SystemAPI.GetSingleton<GameState>().State;
        var planet = SystemAPI.GetSingleton<PlanetData>();

        // 1) Bootstrap on the Sandbox planet.
        if (planet.PlanetID == EPlanetID.Sandbox
            && (gameState == EGameState.Running || gameState == EGameState.RunStarting))
        {
            if (_settingsQuery.IsEmpty)
            {
                var created = em.CreateEntity(typeof(SandboxSettings), typeof(RunScope));
                em.SetComponentData(created, SandboxSettings.SandboxPlanet);
            }
            else
            {
                var existing = _settingsQuery.GetSingletonEntity();
                if (!em.GetComponentData<SandboxSettings>(existing).IsSandboxPlanet)
                    em.SetComponentData(existing, SandboxSettings.SandboxPlanet);
            }
        }

        if (_settingsQuery.IsEmpty)
        {
            // Run ended (RunCleanerSystem destroyed the singleton and the entities): forget applied states.
            _lastFrozen = _lastInvincible = _lastImmortal = _lastGod = _lastPlayerFrozen = false;
            return;
        }

        var settingsEntity = _settingsQuery.GetSingletonEntity();
        var s = em.GetComponentData<SandboxSettings>(settingsEntity);

        // 2) Enemy modes.
        ApplyTag<Frozen>(em, s.EnemiesFrozen, ref _lastFrozen, _enemiesToFreeze, _enemiesFrozen);
        ApplyTag<Invincible>(em, s.EnemiesInvincible, ref _lastInvincible, _enemiesToProtect, _enemiesProtected);

        if (s.EnemiesImmortal)
        {
            bool added = !_enemiesToFloor.IsEmpty;
            if (added)
                em.AddComponent<HealthFloor>(_enemiesToFloor);

            if (added || !_lastImmortal || s.ImmortalRegenDelay != _lastRegenDelay)
            {
                foreach (var floor in SystemAPI.Query<RefRW<HealthFloor>>().WithAll<Enemy>().WithNone<DummyTag>())
                {
                    floor.ValueRW.Floor = 1;
                    floor.ValueRW.RegenDelay = s.ImmortalRegenDelay;
                }
                _lastRegenDelay = s.ImmortalRegenDelay;
            }
        }
        else if (_lastImmortal && !_enemiesFloored.IsEmpty)
        {
            em.RemoveComponent<HealthFloor>(_enemiesFloored);
        }
        _lastImmortal = s.EnemiesImmortal;

        // 3) Player modes (re-posed on a new player after set_character).
        if (s.PlayerHealthFloor && !_playerToFloor.IsEmpty)
        {
            var players = _playerToFloor.ToEntityArray(Allocator.Temp);
            foreach (var p in players)
                em.AddComponentData(p, new HealthFloor { Floor = 1, RegenDelay = -1f, HealOnFloor = true });
            players.Dispose();
        }
        ApplyTag<Invincible>(em, s.PlayerGod, ref _lastGod, _playerToGod, _playerGod);
        ApplyTag<Frozen>(em, s.PlayerFrozen, ref _lastPlayerFrozen, _playerToFreeze, _playerFrozen);

        // 4) Deaths counter.
        if (SystemAPI.TryGetSingletonEntity<Player>(out var player) && em.HasComponent<HealthFloor>(player))
        {
            em.CompleteDependencyBeforeRW<HealthFloor>();
            var floor = em.GetComponentData<HealthFloor>(player);
            if (floor.FloorHits > 0)
            {
                s.PlayerDeaths += floor.FloorHits;
                floor.FloorHits = 0;
                em.SetComponentData(player, floor);
                em.SetComponentData(settingsEntity, s);
                UnityEngine.Debug.Log($"[Sandbox] Player death caught by the floor (total {s.PlayerDeaths}).");
            }
        }
    }

    private static void ApplyTag<T>(EntityManager em, bool on, ref bool last, EntityQuery missing, EntityQuery having)
        where T : unmanaged, IComponentData
    {
        if (on)
        {
            if (!missing.IsEmpty)
                em.AddComponent<T>(missing);
        }
        else if (last && !having.IsEmpty)
        {
            em.RemoveComponent<T>(having);
        }
        last = on;
    }
}
#endif
