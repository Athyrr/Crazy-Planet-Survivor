#if UNITY_EDITOR || DEVELOPMENT_BUILD
using Unity.Burst;
using Unity.Entities;

/// <summary>HealthFloor heal producer: full heal through HealBufferElement — HealthSystem's heal pass
/// stays the single writer of Health — right after a caught lethal blow (HealOnFloor: Sandbox player) or RegenDelay s
/// after the last damaging hit (dummy, enemies_immortal). Runs before HealthSystem, like HealthRegenSystem.</summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(HealthSystem))]
[BurstCompile]
public partial struct HealthFloorRegenSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<GameState>();
        state.RequireForUpdate<HealthFloor>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (SystemAPI.GetSingleton<GameState>().State != EGameState.Running)
            return;

        state.Dependency = new RegenJob { Now = SystemAPI.Time.ElapsedTime }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct RegenJob : IJobEntity
    {
        public double Now;

        private void Execute(ref HealthFloor floor, in Health health, in CoreStats stats, ref DynamicBuffer<HealBufferElement> heals)
        {
            int max = (int)stats.MaxHealth;
            if (health.Value <= 0 || health.Value >= max)
            {
                floor.PendingFloorHeal = false;
                return;
            }

            bool heal = (floor.HealOnFloor && floor.PendingFloorHeal)
                        || (floor.RegenDelay >= 0f && Now - floor.LastHitTime >= floor.RegenDelay);
            if (!heal)
                return;

            floor.PendingFloorHeal = false;
            heals.Add(new HealBufferElement { Amount = max - health.Value });
        }
    }
}
#endif
