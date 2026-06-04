using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Regenerates health over time for entities carrying <see cref="HealthRegen"/>.
/// Each tick heals <c>CoreStats.HealthRecovery</c> per second of elapsed time, clamped to
/// <c>CoreStats.MaxHealth</c>, with fractional HP carried across ticks. Runs after
/// <see cref="HealthSystem"/> so damage is resolved before regeneration is applied.
/// Emits <see cref="HealFeedbackRequest"/> on the player so the UI can show green numbers.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(HealthSystem))]
[BurstCompile]
public partial struct HealthRegenSystem : ISystem
{
    private ComponentLookup<Player> _playerLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameState>();
        state.RequireForUpdate<HealthRegen>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

        _playerLookup = state.GetComponentLookup<Player>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Only regenerate while a run is actively in progress.
        if (SystemAPI.GetSingleton<GameState>().State != EGameState.Running)
            return;

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        _playerLookup.Update(ref state);

        var job = new RegenJob
        {
            DeltaTime = SystemAPI.Time.DeltaTime,
            ECB = ecb.AsParallelWriter(),
            PlayerLookup = _playerLookup,
        };
        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct RegenJob : IJobEntity
    {
        public float DeltaTime;
        public EntityCommandBuffer.ParallelWriter ECB;
        [ReadOnly] public ComponentLookup<Player> PlayerLookup;

        private void Execute(
            [ChunkIndexInQuery] int index,
            Entity entity,
            ref Health health,
            ref HealthRegen regen,
            in CoreStats stats,
            in LocalTransform transform)
        {
            // Skip dead entities and those without a regen stat.
            if (health.Value <= 0 || stats.HealthRegen <= 0f)
                return;

            int maxHealth = (int)stats.MaxHealth;

            // Already full: reset accumulators so a future tick starts clean.
            if (health.Value >= maxHealth)
            {
                regen.Timer = 0f;
                regen.Carryover = 0f;
                return;
            }

            regen.Timer += DeltaTime;

            float interval = regen.TickInterval > 0f ? regen.TickInterval : 1f;
            if (regen.Timer < interval)
                return;

            // Heal proportionally to the elapsed time, banking the fractional remainder.
            regen.Carryover += stats.HealthRegen * regen.Timer;
            regen.Timer = 0f;

            int wholeHeal = (int)regen.Carryover;
            if (wholeHeal <= 0)
                return;

            regen.Carryover -= wholeHeal;
            health.Value = math.min(health.Value + wholeHeal, maxHealth);

            // Emit green heal number feedback for the player.
            if (PlayerLookup.HasComponent(entity))
            {
                Entity req = ECB.CreateEntity(index);
                ECB.AddComponent(index, req, new HealFeedbackRequest
                {
                    Amount = wholeHeal,
                    Transform = transform,
                });
            }
        }
    }
}
