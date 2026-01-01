using Unity.Burst;
using Unity.Entities;
using Unity.Collections;
using Unity.Mathematics;

[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct HealthSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (gameState.State != EGameState.Running)
            return;

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        var applyDamageJob = new ApplyDamageJob
        {
            ECB = ecb.AsParallelWriter(),
            DestroyFlagLookup = SystemAPI.GetComponentLookup<DestroyEntityFlag>(true),
            PlayerLookup = SystemAPI.GetComponentLookup<Player>(true),
            EnemyLookup = SystemAPI.GetComponentLookup<Enemy>(true),
        };
        
        state.Dependency = applyDamageJob.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct ApplyDamageJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        [ReadOnly] public ComponentLookup<DestroyEntityFlag> DestroyFlagLookup;
        [ReadOnly] public ComponentLookup<Player> PlayerLookup;
        [ReadOnly] public ComponentLookup<Enemy> EnemyLookup;
        
        public void Execute([ChunkIndexInQuery] int index, Entity entity, ref Health health, in Stats stats, ref DynamicBuffer<DamageBufferElement> damageBuffer)
        {
            if (DestroyFlagLookup.HasComponent(entity))
                return;

            if (health.Value <= 0 || damageBuffer.IsEmpty)
                return;

            float totalDamage = 0;
            foreach (var dbe in damageBuffer)
            {
                float damage = dbe.Damage;
                ESpellElement element = dbe.Element;

                // Apply Elemental resistance reduction
                switch (element)
                {
                    case ESpellElement.Fire:
                        damage *= 1 - stats.FireResistance / 100;
                        break;
                    case ESpellElement.Ice:
                        damage *= 1 - stats.IceResistance / 100;
                        break;
                    case ESpellElement.Lightning:
                        damage *= 1 - stats.LightningResistance / 100;
                        break;
                    case ESpellElement.Arcane:
                        damage *= 1 - stats.ArcaneResistance / 100;
                        break;
                    default:
                        damage *= 1;
                        break;
                }

                //Apply Armor reduction
                damage -= stats.Armor;
                damage = math.max(0, damage);

                totalDamage += damage;
            }

            // Apply damage 
            health.Value -= math.max(0, totalDamage);

            damageBuffer.Clear();

            if (health.Value <= 0)
            {
                health.Value = 0;
                // Mark entity for destruction
                ECB.AddComponent(index, entity, new DestroyEntityFlag());
            }
        }
    }
}
