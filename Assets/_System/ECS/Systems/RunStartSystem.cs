using Unity.Entities;

/// <summary>
/// Drives the <see cref="EGameState.RunStarting"/> intro window: counts down the configured delay
/// while the planet and player are visible, then flips the <see cref="GameState"/> to the run's
/// target state so enemies and the run timer kick in. Runs only while the game sits in RunStarting;
/// leaving that state (e.g. returning to the lobby mid-intro) drops the pending countdown.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct RunStartSystem : ISystem
{
    private EntityQuery _countdownQuery;

    public void OnCreate(ref SystemState state)
    {
        _countdownQuery = state.GetEntityQuery(ComponentType.ReadWrite<RunStartCountdown>());
        state.RequireForUpdate<RunStartCountdown>();
        state.RequireForUpdate<GameState>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var gameState = SystemAPI.GetSingleton<GameState>();

        // Intro aborted (returned to lobby, new load...): discard the stale countdown.
        if (gameState.State != EGameState.RunStarting)
        {
            state.EntityManager.DestroyEntity(_countdownQuery);
            return;
        }

        float dt = SystemAPI.Time.DeltaTime;
        EGameState target = EGameState.Running;
        bool finished = false;

        foreach (var countdown in SystemAPI.Query<RefRW<RunStartCountdown>>())
        {
            countdown.ValueRW.Remaining -= dt;
            if (countdown.ValueRO.Remaining <= 0f)
            {
                target = countdown.ValueRO.TargetState;
                finished = true;
            }
        }

        if (!finished)
            return;

        state.EntityManager.DestroyEntity(_countdownQuery);

        if (SystemAPI.TryGetSingletonEntity<GameState>(out var gsEntity))
            state.EntityManager.SetComponentData(gsEntity, new GameState { State = target });
    }
}
