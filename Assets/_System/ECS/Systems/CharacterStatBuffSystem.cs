using Unity.Burst;
using Unity.Entities;

/// <summary>
/// Ticks the <see cref="CharacterStatBuff"/> buffer on every character entity: decrements each
/// entry's <c>Remaining</c> and removes expired ones (swap-back). When at least one entry expires
/// on an entity this frame, emits a <see cref="SpellStatsCalculationRequest"/> tag so the on-demand
/// spell recalc picks up the new stat sum (LiveStats is recomposed each frame by
/// <see cref="ActiveEffectsSystem"/>, so tick stats do not need an extra trigger).
/// <br/><br/>
/// Runs before <see cref="ActiveEffectsSystem"/> so this frame's composition sees the updated buffer.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateBefore(typeof(ActiveEffectsSystem))]
[BurstCompile]
public partial struct CharacterStatBuffSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged).AsParallelWriter();

        state.Dependency = new TickBuffsJob
        {
            DeltaTime = SystemAPI.Time.DeltaTime,
            ECB = ecb,
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct TickBuffsJob : IJobEntity
    {
        public float DeltaTime;
        public EntityCommandBuffer.ParallelWriter ECB;

        private void Execute(
            [ChunkIndexInQuery] int chunkIndex,
            Entity entity,
            ref DynamicBuffer<CharacterStatBuff> buffs)
        {
            if (buffs.Length == 0)
                return;

            bool anyExpired = false;

            // Swap-back removal so we don't shift the whole buffer on every deletion.
            for (int i = buffs.Length - 1; i >= 0; i--)
            {
                var b = buffs[i];
                b.Remaining -= DeltaTime;
                if (b.Remaining <= 0f)
                {
                    buffs.RemoveAtSwapBack(i);
                    anyExpired = true;
                }
                else
                {
                    buffs[i] = b;
                }
            }

            // Trigger spell-stats recompute so cached ActiveSpell.FinalX picks up the removed delta.
            // (LiveStats — tick stats — is recomposed unconditionally each frame by ActiveEffectsSystem.)
            if (anyExpired)
                ECB.AddComponent<SpellStatsCalculationRequest>(chunkIndex, entity);
        }
    }
}
