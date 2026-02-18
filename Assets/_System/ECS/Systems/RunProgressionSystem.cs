using Unity.Burst;
using Unity.Collections;
using Unity.Entities;

/// <summary>
/// System that manages the progression timer of a run.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct RunProgressionSystem : ISystem
{
    private EntityQuery _killedEventsQuery;
    private EntityQuery _playerDamageQuery;
    private EntityQuery _enemyDamageQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        //state.RequireForUpdate<StartRunRequest>();
        //state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<GameState>();

        _killedEventsQuery = state.GetEntityQuery(ComponentType.ReadOnly<EnemyKilledEvent>());
        _playerDamageQuery = state.GetEntityQuery(
            ComponentType.ReadOnly<Player>(),
            ComponentType.ReadOnly<DamageBufferElement>()
        );
        _enemyDamageQuery = state.GetEntityQuery(
            ComponentType.ReadOnly<Enemy>(),
            ComponentType.ReadOnly<DamageBufferElement>()
        );
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out GameState gameState))
            return;

        if (gameState.State != EGameState.Running)
            return;

        if (!SystemAPI.TryGetSingleton<PlanetData>(out PlanetData planetData))
            return;

        if (!SystemAPI.TryGetSingletonEntity<RunProgression>(out var progressionEntity))
        {
            progressionEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(
                progressionEntity,
                new RunProgression
                {
                    Timer = 0,
                    ProgressRatio = 0,
                    PlanetID = planetData.PlanetID,
                    EnemiesKilledCount = 0,
                    TotalDamageDealt = 0,
                    TotalDamageTaken = 0,
                    TotalExperienceCollected = 0,
                }
            );

            // Run scope for end run destruction of the run progression
            state.EntityManager.AddComponent<RunScope>(progressionEntity);
        }

        var progression = SystemAPI.GetComponentRW<RunProgression>(progressionEntity);

        // Track Kills
        int enemiesKilledInFrame = _killedEventsQuery.CalculateEntityCount();
        if (enemiesKilledInFrame > 0)
        {
            progression.ValueRW.EnemiesKilledCount += enemiesKilledInFrame;
            state.EntityManager.DestroyEntity(_killedEventsQuery);
        }

        // Track Damage Taken (Player)
        if (!_playerDamageQuery.IsEmptyIgnoreFilter)
        {
            var playerDamageBuffers = _playerDamageQuery.ToComponentDataArray<DamageBufferElement>(
                Allocator.Temp
            );
            foreach (var dbe in playerDamageBuffers)
            {
                // We need to be careful: HealthSystem clears the buffer.
                // If this system runs AFTER HealthSystem, the buffer is empty.
                // If it runs BEFORE, we see the damage.
                // HealthSystem is in SimulationSystemGroup (Default).
                // RunProgressionSystem is also in SimulationSystemGroup (Default).
                // We should ensure RunProgressionSystem runs BEFORE HealthSystem to catch the buffers.
            }
        }

        // Actually, a better way to track damage without order issues is to use a separate
        // "DamageEvent" component or sum it up in the HealthSystem using a Singleton.
        // Let's stick to summing it up in the systems that process the buffers but using a non-parallel job or main thread.

        if (SystemAPI.TryGetSingleton<EndRunRequest>(out var _))
            return;

        var ecbSingleton =
            SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var runProgressionJob = new RunProgressionJob
        {
            ECB = ecb.AsParallelWriter(),
            DeltaTime = SystemAPI.Time.DeltaTime,
            RunDuration = planetData.RunDuration,
        };
        state.Dependency = runProgressionJob.Schedule(state.Dependency);
    }

    [BurstCompile]
    private partial struct RunProgressionJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        public float DeltaTime;
        public float RunDuration;

        public void Execute(ref RunProgression run)
        {
            run.Timer += DeltaTime;
            run.ProgressRatio = run.Timer / RunDuration;

            if (run.ProgressRatio >= 1)
            {
                run.ProgressRatio = 1;
                var endRunReqEntity = ECB.CreateEntity(0);
                ECB.AddComponent(
                    0,
                    endRunReqEntity,
                    new EndRunRequest { State = EEndRunState.Timeout }
                );
            }
        }
    }
}
