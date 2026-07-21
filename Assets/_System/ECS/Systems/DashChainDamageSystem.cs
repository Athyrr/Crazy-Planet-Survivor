using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Propagates a dash's "chain damage": an enemy knocked back by a dash carrying
/// <see cref="DashEffect.KnockbackChainDamage"/> is marked with <see cref="DashChainDamage"/>, and while it
/// is still airborne (has an active knockback) it periodically damages nearby enemies — and itself — via the
/// standard <see cref="DamageBufferElement"/> path. A tick throttle bounds the cost; the marker is disabled
/// once the knockback ends. Overlap-based, so no per-enemy collider surgery.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(KnockbackSystem))]
[BurstCompile]
public partial struct DashChainDamageSystem : ISystem
{
    private ComponentLookup<ActiveKnockback> _knockbackLookup;
    private BufferLookup<DamageBufferElement> _damageBufferLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();

        _knockbackLookup = state.GetComponentLookup<ActiveKnockback>(true);
        _damageBufferLookup = state.GetBufferLookup<DamageBufferElement>(false);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState) || gameState.State != EGameState.Running)
            return;

        _knockbackLookup.Update(ref state);
        _damageBufferLookup.Update(ref state);

        state.Dependency = new ChainDamageJob
        {
            DeltaTime = SystemAPI.Time.DeltaTime,
            CollisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld,
            KnockbackLookup = _knockbackLookup,
            DamageBufferLookup = _damageBufferLookup,
        }.Schedule(state.Dependency);
    }

    [BurstCompile]
    private partial struct ChainDamageJob : IJobEntity
    {
        [ReadOnly] public float DeltaTime;
        [ReadOnly] public CollisionWorld CollisionWorld;
        [ReadOnly] public ComponentLookup<ActiveKnockback> KnockbackLookup;

        // Direct RW buffer writes replace the previous ECB.AppendToBuffer: writes happen inside the job
        // instead of being deferred to EndSimulation, so an enemy that dies same-frame from a spell/dash
        // can no longer trigger "Buffer does not exist on entity" during ECB playback.
        [NativeDisableParallelForRestriction] public BufferLookup<DamageBufferElement> DamageBufferLookup;

        // How often a flung enemy deals its chain damage while airborne.
        private const float TickRate = 0.08f;

        public void Execute(Entity self, in LocalTransform transform, ref DashChainDamage chain,
            EnabledRefRW<DashChainDamage> chainEnabled)
        {
            // Stop propagating once the enemy is no longer being knocked back.
            bool knocked = KnockbackLookup.HasComponent(self) && KnockbackLookup.IsComponentEnabled(self);
            if (!knocked || chain.Radius <= 0f)
            {
                chainEnabled.ValueRW = false;
                return;
            }

            chain.TickTimer -= DeltaTime;
            if (chain.TickTimer > 0f)
                return;
            chain.TickTimer = TickRate;

            var hits = new NativeList<DistanceHit>(8, Allocator.Temp);
            var filter = new CollisionFilter
            {
                BelongsTo = CollisionLayers.Raycast,
                CollidesWith = CollisionLayers.Enemy,
            };

            bool hitAny = false;
            if (CollisionWorld.OverlapSphere(transform.Position, chain.Radius, ref hits, filter))
            {
                var processed = new NativeHashSet<Entity>(hits.Length, Allocator.Temp);
                int dmg = (int)chain.Damage;
                var damage = new DamageBufferElement
                {
                    Damage = dmg,
                    Tag = ESpellTag.None,
                    IsCritical = false,
                    ShakeSource = EDamageShakeSource.None,
                };

                for (int i = 0; i < hits.Length; i++)
                {
                    Entity other = hits[i].Entity;
                    if (other == self || !processed.Add(other))
                        continue;
                    if (!DamageBufferLookup.HasBuffer(other))
                        continue; // not a damageable target (or already teared down)

                    DamageBufferLookup[other].Add(damage);
                    hitAny = true;
                }

                processed.Dispose();

                // The flung enemy takes damage from the impact too ("both take damage").
                if (hitAny && DamageBufferLookup.HasBuffer(self))
                    DamageBufferLookup[self].Add(damage);
            }

            hits.Dispose();
        }
    }
}
