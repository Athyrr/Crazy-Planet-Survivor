using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Follow mobility (pet, spec allegeance-summon §3), with the enemies' steering (SurfaceSteering) but its own goal and
/// no flow field:
///   • within the leash: goal = nearest hostile (HostileLayers of its team) within LeashRadius, approached to ~80 % of
///     its first attack spell's range, then holds and fires (SpellCooldownSystem); no hostile → back near the owner;
///   • beyond LeashRadius: goal = owner until back within ~50 % of it (hysteresis, SummonMath.UpdateLeash).
/// Reads the owner's LocalToWorld (last frame's, no alias with the LocalTransform it writes). Runs before
/// EmitterAnchorSystem so BeamAimSystem / attached emitters see the pet's new position.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(ActiveEffectsSystem))]
[UpdateBefore(typeof(EmitterAnchorSystem))]
[UpdateBefore(typeof(TransformSystemGroup))]
[BurstCompile]
public partial struct SummonMovementSystem : ISystem
{
    private ComponentLookup<LocalToWorld> _ltwLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Summon>();
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<SpellsDatabase>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        _ltwLookup = state.GetComponentLookup<LocalToWorld>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState) || gameState.State != EGameState.Running)
            return;

        _ltwLookup.Update(ref state);
        var planet = SystemAPI.GetSingleton<PlanetData>();

        state.Dependency = new FollowJob
        {
            DeltaTime = SystemAPI.Time.DeltaTime,
            PlanetCenter = planet.Center,
            CollisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld,
            SpellDb = SystemAPI.GetSingleton<SpellsDatabase>().Blobs,
            LtwLookup = _ltwLookup,
        }.ScheduleParallel(state.Dependency);
    }

    [BurstCompile]
    private partial struct FollowJob : IJobEntity
    {
        public float DeltaTime;
        public float3 PlanetCenter;
        [ReadOnly] public CollisionWorld CollisionWorld;
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellDb;
        [ReadOnly] public ComponentLookup<LocalToWorld> LtwLookup;

        // Same default as the enemies (FlowFieldMovementSystem): below this speed, face the goal.
        private const float FacingSpeedThreshold = 0.5f;
        // Spec §3: approach the target to ~80 % of the spell's range; return until ~50 % of the leash radius.
        private const float ApproachRangeFraction = 0.8f;
        private const float LeashReturnFraction = 0.5f;

        private void Execute(ref LocalTransform transform, ref SummonFollow follow, in Summon summon, in Team team,
            in LiveStats live, in DynamicBuffer<ActiveSpell> spells)
        {
            ref var db = ref SpellDb.Value.Spells;
            if (summon.SourceDatabaseIndex < 0 || summon.SourceDatabaseIndex >= db.Length)
                return;
            ref var spellBlob = ref db[summon.SourceDatabaseIndex];
            if (!spellBlob.IsSummon || spellBlob.Summon.Mobility != ESummonMobility.Follow)
                return;
            if (!LtwLookup.TryGetComponent(summon.Owner, out var ownerLtw))
                return;
            ref var sb = ref spellBlob.Summon;

            float3 p = transform.Position;
            float3 n = math.normalizesafe(p - PlanetCenter, new float3(0f, 1f, 0f));
            float3 ownerPos = ownerLtw.Position;

            follow.Returning = SummonMath.UpdateLeash(follow.Returning, SummonMath.ArcDistance(p, ownerPos, PlanetCenter),
                sb.LeashRadius, LeashReturnFraction);

            // Goal: owner (returning, or nothing to fight), else the nearest hostile.
            float3 goal = ownerPos;
            float stopDistance = follow.Returning ? 0f : sb.RingMaxRadius;
            float range = spells.Length > 0 ? spells[0].FinalRange : 0f;
            if (!follow.Returning && range > 0f)
            {
                var input = new PointDistanceInput
                {
                    Position = p,
                    MaxDistance = sb.LeashRadius,
                    Filter = new CollisionFilter { BelongsTo = CollisionLayers.Raycast, CollidesWith = TeamUtils.HostileLayers(team.Value) },
                };
                if (CollisionWorld.CalculateDistance(input, out DistanceHit hit))
                {
                    goal = LtwLookup.TryGetComponent(hit.Entity, out var targetLtw) ? targetLtw.Position : hit.Position;
                    stopDistance = range * ApproachRangeFraction;
                }
            }

            float3 toGoalRaw = goal - p;
            PlanetUtils.ProjectDirectionOnSurface(in toGoalRaw, in n, out float3 toGoal);
            float speedFactor = SummonMath.ApproachFactor(SummonMath.ArcDistance(p, goal, PlanetCenter), stopDistance);
            float3 desiredVelocity = toGoal * (live.MoveSpeed * speedFactor);

            follow.Velocity = SurfaceSteering.RampVelocity(follow.Velocity, n, desiredVelocity, sb.Acceleration, DeltaTime);
            float3 fallbackFace = math.lengthsq(toGoal) > 1e-4f ? toGoal : transform.Forward();
            float3 face = SurfaceSteering.FaceDirection(follow.Velocity, FacingSpeedThreshold, fallbackFace);

            SurfaceSteering.SnapToGroundAndTurn(ref transform, in CollisionWorld, p + follow.Velocity * DeltaTime, n, face,
                math.radians(sb.MaxTurnRateDeg) * DeltaTime, DeltaTime);
        }
    }
}
