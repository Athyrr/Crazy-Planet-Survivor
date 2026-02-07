using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// Processes incoming damage from the <see cref="DamageBufferElement"/>, applying elemental
/// resistances and armor reductions before updating the entity's health.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct HealthSystem : ISystem
{
    private ComponentLookup<Player> _playerLookup;
    private ComponentLookup<Enemy> _enemyLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

        _playerLookup = state.GetComponentLookup<Player>(true);
        _enemyLookup = state.GetComponentLookup<Enemy>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        // Only process health changes while the game is actively running
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState))
            return;

        if (gameState.State != EGameState.Running)
            return;

        // Use the EndSimulation ECB to handle entity destruction at the end of the frame
        var ecbSingleton =
            SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        _playerLookup.Update(ref state);
        _enemyLookup.Update(ref state);

        var applyDamageJob = new ApplyDamageJob
        {
            ECB = ecb.AsParallelWriter(),
            DestroyFlagLookup = SystemAPI.GetComponentLookup<DestroyEntityFlag>(true),
            PlayerLookup = _playerLookup,
            EnemyLookup = _enemyLookup,
            //PlayerLookup = SystemAPI.GetComponentLookup<Player>(true),
            //EnemyLookup = SystemAPI.GetComponentLookup<Enemy>(true),
        };

        state.Dependency = applyDamageJob.ScheduleParallel(state.Dependency);
    }

    /// <summary>
    /// Calculates the total damage for an entity by iterating through its damage buffer
    /// and applying defensive stats.
    /// </summary>
    [BurstCompile]
    private partial struct ApplyDamageJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        /// <summary> Used to check if an entity is already flagged for destruction. </summary>
        [ReadOnly]
        public ComponentLookup<DestroyEntityFlag> DestroyFlagLookup;

        /// <summary> Provided for potential future filtering or logic (currently unused). </summary>
        [ReadOnly]
        public ComponentLookup<Player> PlayerLookup;

        /// <summary> Provided for potential future filtering or logic (currently unused). </summary>
        [ReadOnly]
        public ComponentLookup<Enemy> EnemyLookup;

        public void Execute(
            [ChunkIndexInQuery] int index,
            Entity entity,
            ref Health health,
            in Stats stats,
            ref DynamicBuffer<DamageBufferElement> damageBuffer
        )
        {
            // Skip entities already marked for destruction
            if (DestroyFlagLookup.HasComponent(entity))
                return;

            // Skip if health is already zero or there is no damage to process
            if (health.Value <= 0 || damageBuffer.IsEmpty)
                return;

            var isPlayer = PlayerLookup.HasComponent(entity);

            float totalDamage = 0;
            // Process every damage instance stored in the buffer this frame
            foreach (var dbe in damageBuffer)
            {
                float damage = dbe.Damage;
                ESpellTag element = dbe.Element;

                // Apply Elemental resistance reduction
                switch (element)
                {
                    case ESpellTag.Fire:
                        damage *= 1 - stats.FireResistance / 100;
                        break;
                    case ESpellTag.Ice:
                        damage *= 1 - stats.IceResistance / 100;
                        break;
                    case ESpellTag.Lightning:
                        damage *= 1 - stats.LightningResistance / 100;
                        break;
                    case ESpellTag.Arcane:
                        damage *= 1 - stats.ArcaneResistance / 100;
                        break;
                    default:
                        damage *= 1;
                        break;
                }

                // Apply flat Armor reduction after elemental resistances
                damage -= stats.Armor;
                damage = math.max(0, damage);

                totalDamage += damage;
            }

            // Apply the accumulated damage to the health component
            health.Value -= math.max(0, totalDamage);

            damageBuffer.Clear();

            // Check for death condition
            if (health.Value <= 0)
            {
                health.Value = 0;
                ECB.AddComponent(index, entity, new DestroyEntityFlag());

                if (isPlayer)
                {
                    var endRunReqEntity = ECB.CreateEntity(0);
                    ECB.AddComponent(
                        index,
                        endRunReqEntity,
                        new EndRunRequest() { State = EEndRunState.Death }
                    );
                }
                else if (EnemyLookup.HasComponent(entity))
                {
                    var killedEventEntity = ECB.CreateEntity(index);
                    ECB.AddComponent(
                        index,
                        killedEventEntity,
                        new EnemyKilledEvent { WaveIndex = EnemyLookup[entity].WaveIndex }
                    );
                }
            }
        }
    }
}

public struct EnemyKilledEvent : IComponentData
{
    public int WaveIndex;
}
