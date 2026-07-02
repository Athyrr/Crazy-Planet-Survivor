using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Applies life steal: drains the player's queued procs and heals at most one per cooldown window
/// (the rate limiter → Brotato-style diminishing returns when many hits land). The heal magnitude is the
/// strongest queued proc this frame (<c>Conversion × that hit's damage</c>); fractional HP is banked in
/// <see cref="LifeStealState.HealCarryover"/>. Runs after <see cref="HealthSystem"/> so the frame's damage
/// is resolved first, and emits a <see cref="HealFeedbackRequest"/> (green number) when whole HP is gained.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(HealthSystem))]
[BurstCompile]
public partial struct LifeStealSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameState>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        state.RequireForUpdate<LifeStealProcBufferElement>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (SystemAPI.GetSingleton<GameState>().State != EGameState.Running)
            return;

        float cooldown = SystemAPI.TryGetSingleton<LifeStealConfig>(out var cfg) ? cfg.ProcCooldown : 0.1f;

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var job = new LifeStealHealJob
        {
            DeltaTime = SystemAPI.Time.DeltaTime,
            ProcCooldown = cooldown > 0f ? cooldown : 0.1f,
            ECB = ecb.AsParallelWriter(),
        };
        state.Dependency = job.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    [WithAll(typeof(Player))]
    private partial struct LifeStealHealJob : IJobEntity
    {
        public float DeltaTime;
        public float ProcCooldown;
        public EntityCommandBuffer.ParallelWriter ECB;

        private void Execute(
            [ChunkIndexInQuery] int chunkIndex,
            ref Health health,
            in CoreStats stats,
            ref LifeStealState lifeSteal,
            ref DynamicBuffer<LifeStealProcBufferElement> procs,
            in LocalTransform transform)
        {
            if (lifeSteal.CooldownTimer > 0f)
                lifeSteal.CooldownTimer -= DeltaTime;

            // Strongest proc queued this frame; the rest are discarded by the cooldown (diminishing returns).
            float maxHeal = 0f;
            for (int i = 0; i < procs.Length; i++)
                maxHeal = math.max(maxHeal, procs[i].Heal);
            procs.Clear();

            if (health.Value <= 0 || maxHeal <= 0f || lifeSteal.CooldownTimer > 0f)
                return;

            lifeSteal.CooldownTimer = ProcCooldown;

            lifeSteal.HealCarryover += maxHeal;
            int wholeHeal = (int)lifeSteal.HealCarryover;
            if (wholeHeal <= 0)
                return;
            lifeSteal.HealCarryover -= wholeHeal;

            int maxHp = (int)stats.MaxHealth;
            if (health.Value >= maxHp)
            {
                lifeSteal.HealCarryover = 0f;
                return;
            }

            health.Value = math.min(health.Value + wholeHeal, maxHp);

            Entity req = ECB.CreateEntity(chunkIndex);
            ECB.AddComponent(chunkIndex, req, new HealFeedbackRequest
            {
                Amount = wholeHeal,
                Transform = transform,
            });
        }
    }
}
