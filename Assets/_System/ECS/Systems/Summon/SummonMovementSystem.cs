using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Follow mobility (pet, spec allegeance-summon §3), with the enemies' steering (SurfaceSteering) but its own goal and
/// no flow field:
///   • within the leash: goal = nearest hostile (HostileLayers of its team) within LeashRadius, approached to ~80 % of
///     its first attack spell's range, then holds and fires (SpellCooldownSystem); no hostile → back near the owner;
///   • beyond LeashRadius: goal = owner until back within ~50 % of it (hysteresis, SummonMath.UpdateLeash), speed and
///     acceleration × ReturnSpeedMultiplier;
///   • beyond TeleportDistanceFactor × LeashRadius (0 = never): teleport to the middle of the largest free arc of the
///     owner's ring (same free-arc rule as SummonReconcileSystem's relocation: SummonMath.FreeRingAngle over the other
///     agents of that owner), velocity zeroed, back within the leash.
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
    private ComponentLookup<Summon> _summonLookup;
    private EntityQuery _agentQuery;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Summon>();
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<SpellsDatabase>();
        state.RequireForUpdate<PhysicsWorldSingleton>();
        _ltwLookup = state.GetComponentLookup<LocalToWorld>(true);
        _summonLookup = state.GetComponentLookup<Summon>(true);
        _agentQuery = SystemAPI.QueryBuilder().WithAll<Summon, LocalToWorld>().Build();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState) || gameState.State != EGameState.Running)
            return;

        _ltwLookup.Update(ref state);
        _summonLookup.Update(ref state);
        var planet = SystemAPI.GetSingleton<PlanetData>();

        // Every agent (any owner, any mobility): the teleport destination avoids the other agents of the same owner.
        var agents = _agentQuery.ToEntityListAsync(state.WorldUpdateAllocator, state.Dependency, out JobHandle agentsHandle);

        state.Dependency = new FollowJob
        {
            DeltaTime = SystemAPI.Time.DeltaTime,
            PlanetCenter = planet.Center,
            CollisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld,
            SpellDb = SystemAPI.GetSingleton<SpellsDatabase>().Blobs,
            LtwLookup = _ltwLookup,
            SummonLookup = _summonLookup,
            Agents = agents.AsDeferredJobArray(),
            Seed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + 1u,
        }.ScheduleParallel(agentsHandle);
    }

    [BurstCompile]
    private partial struct FollowJob : IJobEntity
    {
        public float DeltaTime;
        public float3 PlanetCenter;
        [ReadOnly] public CollisionWorld CollisionWorld;
        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellDb;
        [ReadOnly] public ComponentLookup<LocalToWorld> LtwLookup;
        [ReadOnly] public ComponentLookup<Summon> SummonLookup;
        [ReadOnly] public NativeArray<Entity> Agents;
        public uint Seed;

        // Same default as the enemies (FlowFieldMovementSystem): below this speed, face the goal.
        private const float FacingSpeedThreshold = 0.5f;
        // Spec §3: approach the target to ~80 % of the spell's range; return until ~50 % of the leash radius.
        private const float ApproachRangeFraction = 0.8f;
        private const float LeashReturnFraction = 0.5f;

        private void Execute(Entity self, ref LocalTransform transform, ref SummonFollow follow, in Summon summon, in Team team,
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

            float distanceToOwner = SummonMath.ArcDistance(p, ownerPos, PlanetCenter);
            if (SummonMath.ShouldTeleport(distanceToOwner, sb.LeashRadius, sb.TeleportDistanceFactor))
            {
                TeleportToRing(self, ref transform, ref follow, in summon, in ownerLtw, ref sb);
                return;
            }

            follow.Returning = SummonMath.UpdateLeash(follow.Returning, distanceToOwner, sb.LeashRadius, LeashReturnFraction);
            float returnBoost = follow.Returning ? sb.ReturnSpeedMultiplier : 1f;

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
            float3 desiredVelocity = toGoal * (live.MoveSpeed * speedFactor * returnBoost);

            follow.Velocity = SurfaceSteering.RampVelocity(follow.Velocity, n, desiredVelocity, sb.Acceleration * returnBoost, DeltaTime);
            float3 fallbackFace = math.lengthsq(toGoal) > 1e-4f ? toGoal : transform.Forward();
            float3 face = SurfaceSteering.FaceDirection(follow.Velocity, FacingSpeedThreshold, fallbackFace);

            SurfaceSteering.SnapToGroundAndTurn(ref transform, in CollisionWorld, p + follow.Velocity * DeltaTime, n, face,
                math.radians(sb.MaxTurnRateDeg) * DeltaTime, DeltaTime);
        }

        /// <summary>Ring frame of SummonReconcileSystem (owner normal, owner forward as reference), free arc among the
        /// owner's other agents (last frame's LocalToWorld), random radius in [RingMin, RingMax]; terrain snap through the
        /// pet's own SnapToGroundAndTurn (long-ray fallback), facing away from the owner.</summary>
        private void TeleportToRing(Entity self, ref LocalTransform transform, ref SummonFollow follow, in Summon summon,
            in LocalToWorld ownerLtw, ref SummonBlob sb)
        {
            float3 ownerPos = ownerLtw.Position;
            float3 nOwner = math.normalizesafe(ownerPos - PlanetCenter, new float3(0f, 1f, 0f));
            float3 reference = EmitterMath.TangentReference(nOwner, ownerLtw.Forward);

            var occupied = new NativeList<float>(8, Allocator.Temp);
            for (int i = 0; i < Agents.Length; i++)
            {
                Entity other = Agents[i];
                if (other == self || !SummonLookup.TryGetComponent(other, out var s) || s.Owner != summon.Owner
                    || !LtwLookup.TryGetComponent(other, out var otherLtw))
                    continue;
                occupied.Add(SummonMath.RingAngle(otherLtw.Position - ownerPos, nOwner, reference));
            }
            float angle = SummonMath.FreeRingAngle(occupied.AsArray());
            occupied.Dispose();

            var rng = Random.CreateFromIndex(Seed ^ (uint)self.Index);
            float radius = math.lerp(sb.RingMinRadius, sb.RingMaxRadius, rng.NextFloat());
            float3 ringPoint = ownerPos + EmitterMath.OrbitOffset(nOwner, reference, radius, angle);
            float3 nRing = math.normalizesafe(ringPoint - PlanetCenter, nOwner);
            float3 face = EmitterMath.TangentReference(nRing, ringPoint - ownerPos);

            follow.Velocity = float3.zero;
            follow.Returning = false;
            transform.Position = ringPoint;
            SurfaceSteering.SnapToGroundAndTurn(ref transform, in CollisionWorld, ringPoint, nRing, face, math.PI, DeltaTime);
        }
    }
}
