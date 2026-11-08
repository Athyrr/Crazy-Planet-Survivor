using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;

/// <summary>
/// Channeled beam (spec channeled-beam). Per caster, rays in EmitterAnchor.Index order:
///   • target pass every RetargetInterval, or at once when a ray has never been served (new ray from the reconcile):
///     candidates = OverlapSphere(caster, FinalRange) Raycast→hostile layers of the caster's team (TeamUtils), deduped, not DestroyEntityFlag, arc ≤ FinalRange,
///     nearest arc first; distribution by AssignTargets;
///   • every frame: direction toward the target, re-projected on the caster's tangent plane, turned at most
///     TurnRate × dt (set directly on re-light, and every frame when TurnRate ≤ 0 = instant follow); transform = caster position + LookRotation(direction, normal);
///     AreaSegment = surface arc of length FinalRange (EmitterMath.LinePoint, decision I4); POWER copied from the
///     ROOT ActiveSpell (decision I2); BeamActive = has a target, rising edge → ElapsedTime = TickRate (first tick
///     this frame, AreaAttackSystem runs after).
/// Single-threaded IJob: the per-caster ordering cannot be split across threads; nothing managed.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(EmitterAnchorSystem))]
[UpdateAfter(typeof(SpellStatsCalculationSystem))]
[UpdateBefore(typeof(TransformSystemGroup))]
[UpdateBefore(typeof(AreaAttackSystem))]
[BurstCompile]
public partial struct BeamAimSystem : ISystem
{
    internal const float RetargetInterval = 0.1f;

    private EntityQuery _rayQuery;
    private ComponentLookup<BeamAim> _aimLookup;
    private ComponentLookup<BeamActive> _activeLookup;
    private ComponentLookup<AreaAttack> _areaLookup;
    private ComponentLookup<AreaSegment> _segmentLookup;
    private ComponentLookup<LocalTransform> _transformLookup;
    private ComponentLookup<EmitterAnchor> _anchorLookup;
    private ComponentLookup<SpellSource> _sourceLookup;
    private ComponentLookup<DestroyEntityFlag> _destroyLookup;
    private BufferLookup<ActiveSpell> _activeSpellLookup;
    private ComponentLookup<Team> _teamLookup;
    private ComponentLookup<Player> _playerLookup;

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        _rayQuery = SystemAPI.QueryBuilder()
            .WithAll<BeamAim, EmitterAnchor, AreaAttack, AreaSegment, LocalTransform>()
            .WithPresent<BeamActive>() // lit AND off rays
            .Build();
        state.RequireForUpdate(_rayQuery);
        state.RequireForUpdate<PhysicsWorldSingleton>();

        _aimLookup = state.GetComponentLookup<BeamAim>(false);
        _activeLookup = state.GetComponentLookup<BeamActive>(false);
        _areaLookup = state.GetComponentLookup<AreaAttack>(false);
        _segmentLookup = state.GetComponentLookup<AreaSegment>(false);
        _transformLookup = state.GetComponentLookup<LocalTransform>(false);
        _anchorLookup = state.GetComponentLookup<EmitterAnchor>(true);
        _sourceLookup = state.GetComponentLookup<SpellSource>(true);
        _destroyLookup = state.GetComponentLookup<DestroyEntityFlag>(true);
        _activeSpellLookup = state.GetBufferLookup<ActiveSpell>(true);
        _teamLookup = state.GetComponentLookup<Team>(true);
        _playerLookup = state.GetComponentLookup<Player>(true);
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState) || gameState.State != EGameState.Running)
            return;

        bool hasPlanet = SystemAPI.TryGetSingleton<PlanetData>(out var planet);
        var collisionWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>().CollisionWorld;

        _aimLookup.Update(ref state);
        _activeLookup.Update(ref state);
        _areaLookup.Update(ref state);
        _segmentLookup.Update(ref state);
        _transformLookup.Update(ref state);
        _anchorLookup.Update(ref state);
        _sourceLookup.Update(ref state);
        _destroyLookup.Update(ref state);
        _activeSpellLookup.Update(ref state);
        _teamLookup.Update(ref state);
        _playerLookup.Update(ref state);

        var rays = _rayQuery.ToEntityArray(Allocator.TempJob);
        state.Dependency = new AimJob
        {
            Rays = rays,
            CollisionWorld = collisionWorld,
            AimLookup = _aimLookup,
            ActiveLookup = _activeLookup,
            AreaLookup = _areaLookup,
            SegmentLookup = _segmentLookup,
            TransformLookup = _transformLookup,
            AnchorLookup = _anchorLookup,
            SourceLookup = _sourceLookup,
            DestroyLookup = _destroyLookup,
            ActiveSpellLookup = _activeSpellLookup,
            TeamLookup = _teamLookup,
            PlayerLookup = _playerLookup,
            PlanetCenter = hasPlanet ? planet.Center : float3.zero,
            HasPlanet = hasPlanet,
            DeltaTime = SystemAPI.Time.DeltaTime,
        }.Schedule(state.Dependency);
        state.Dependency = rays.Dispose(state.Dependency);
    }

    /// <summary>Target distribution for ONE caster's rays, in anchor-index order (spec "Ciblage et répartition").
    /// targets / doubling: current state in, new state out. sortedCandidates: valid targets, nearest arc first; a
    /// target is valid iff it is a candidate. 1) a ray on a distinct valid target keeps it; 2) every other ray takes
    /// the nearest non-targeted candidate, else doubles (keeps its own target if it was already doubling on a valid
    /// one — plan choice P3 —, else the nearest); 3) no candidate → no target.</summary>
    internal static void AssignTargets(NativeArray<Entity> targets, NativeArray<bool> doubling, NativeArray<Entity> sortedCandidates)
    {
        int count = targets.Length;
        var taken = new NativeHashSet<Entity>(math.max(1, count), Allocator.Temp);
        var needs = new NativeArray<bool>(count, Allocator.Temp);

        for (int i = 0; i < count; i++)
        {
            Entity t = targets[i];
            bool keep = t != Entity.Null && !doubling[i] && Contains(sortedCandidates, t) && taken.Add(t);
            needs[i] = !keep;
        }

        for (int i = 0; i < count; i++)
        {
            if (!needs[i])
                continue;

            Entity pick = Entity.Null;
            for (int c = 0; c < sortedCandidates.Length; c++)
            {
                if (!taken.Contains(sortedCandidates[c]))
                {
                    pick = sortedCandidates[c];
                    break;
                }
            }

            if (pick != Entity.Null)
            {
                targets[i] = pick;
                doubling[i] = false;
                taken.Add(pick);
            }
            else if (sortedCandidates.Length > 0)
            {
                bool keepCurrent = doubling[i] && targets[i] != Entity.Null && Contains(sortedCandidates, targets[i]);
                if (!keepCurrent)
                    targets[i] = sortedCandidates[0];
                doubling[i] = true;
            }
            else
            {
                targets[i] = Entity.Null;
                doubling[i] = false;
            }
        }

        needs.Dispose();
        taken.Dispose();
    }

    private static bool Contains(NativeArray<Entity> list, Entity e)
    {
        for (int i = 0; i < list.Length; i++)
            if (list[i] == e)
                return true;
        return false;
    }

    private struct RayRef
    {
        public Entity Caster;
        public int DatabaseIndex;
        public int Index;
        public Entity Ray;
    }

    private struct RayRefComparer : IComparer<RayRef>
    {
        public int Compare(RayRef a, RayRef b)
        {
            int c = a.Caster.Index.CompareTo(b.Caster.Index);
            if (c != 0) return c;
            c = a.Caster.Version.CompareTo(b.Caster.Version);
            if (c != 0) return c;
            c = a.Index.CompareTo(b.Index);
            return c != 0 ? c : a.Ray.Index.CompareTo(b.Ray.Index);
        }
    }

    private struct Candidate
    {
        public Entity Entity;
        public float Arc;
    }

    private struct CandidateComparer : IComparer<Candidate>
    {
        public int Compare(Candidate a, Candidate b) => a.Arc.CompareTo(b.Arc);
    }

    [BurstCompile]
    private struct AimJob : IJob
    {
        [ReadOnly] public NativeArray<Entity> Rays;
        [ReadOnly] public CollisionWorld CollisionWorld;

        public ComponentLookup<BeamAim> AimLookup;
        public ComponentLookup<BeamActive> ActiveLookup;
        public ComponentLookup<AreaAttack> AreaLookup;
        public ComponentLookup<AreaSegment> SegmentLookup;
        // Single thread: one lookup reads the caster and the targets, and writes the rays — no aliasing.
        public ComponentLookup<LocalTransform> TransformLookup;
        [ReadOnly] public ComponentLookup<EmitterAnchor> AnchorLookup;
        [ReadOnly] public ComponentLookup<SpellSource> SourceLookup;
        [ReadOnly] public ComponentLookup<DestroyEntityFlag> DestroyLookup;
        [ReadOnly] public BufferLookup<ActiveSpell> ActiveSpellLookup;
        [ReadOnly] public ComponentLookup<Team> TeamLookup;
        [ReadOnly] public ComponentLookup<Player> PlayerLookup;

        public float3 PlanetCenter;
        public bool HasPlanet;
        public float DeltaTime;

        public void Execute()
        {
            var refs = new NativeList<RayRef>(Rays.Length, Allocator.Temp);
            for (int i = 0; i < Rays.Length; i++)
            {
                Entity ray = Rays[i];
                var anchor = AnchorLookup[ray];
                // Orphan ray (owner gone): EmitterAnchorSystem destroys it this frame.
                if (!SourceLookup.TryGetComponent(anchor.Owner, out var ownerSource))
                    continue;
                refs.Add(new RayRef { Caster = ownerSource.CasterEntity, DatabaseIndex = ownerSource.DatabaseIndex, Index = anchor.Index, Ray = ray });
            }
            refs.Sort(new RayRefComparer());

            var candidates = new NativeList<Entity>(32, Allocator.Temp);
            var targets = new NativeList<Entity>(8, Allocator.Temp);
            var doubling = new NativeList<bool>(8, Allocator.Temp);

            int start = 0;
            while (start < refs.Length)
            {
                int end = start + 1;
                while (end < refs.Length && refs[end].Caster == refs[start].Caster)
                    end++;
                ProcessCaster(in refs, start, end, ref candidates, ref targets, ref doubling);
                start = end;
            }

            doubling.Dispose();
            targets.Dispose();
            candidates.Dispose();
            refs.Dispose();
        }

        private void ProcessCaster(in NativeList<RayRef> refs, int start, int end,
            ref NativeList<Entity> candidates, ref NativeList<Entity> targets, ref NativeList<bool> doubling)
        {
            Entity caster = refs[start].Caster;
            // Caster gone: EmitterAnchorSystem destroys the anchors; the visual bridge hides them meanwhile.
            if (!TransformLookup.TryGetComponent(caster, out var casterTransform))
                return;

            if (!EmitterQueries.TryGetActiveSpell(ActiveSpellLookup, caster, refs[start].DatabaseIndex, out var root))
            {
                // Root spell gone: EmitterReconcileSystem destroys the owner; stay off until then.
                for (int k = start; k < end; k++)
                    ActiveLookup.SetComponentEnabled(refs[k].Ray, false);
                return;
            }

            float3 p = casterTransform.Position;
            float3 n = math.normalizesafe(p - PlanetCenter, new float3(0f, 1f, 0f));
            float planetRadius = HasPlanet ? math.length(p - PlanetCenter) : 0f;
            float range = root.FinalRange;

            bool pass = false;
            for (int k = start; k < end; k++)
            {
                var aim = AimLookup[refs[k].Ray];
                aim.RetargetTimer -= DeltaTime;
                if (aim.RetargetTimer <= 0f || !aim.Served)
                    pass = true;
                AimLookup[refs[k].Ray] = aim;
            }

            if (pass)
            {
                CollectCandidates(p, n, planetRadius, range,
                    TeamUtils.HostileLayers(TeamUtils.Resolve(caster, in TeamLookup, in PlayerLookup)), ref candidates);
                targets.Clear();
                doubling.Clear();
                for (int k = start; k < end; k++)
                {
                    var aim = AimLookup[refs[k].Ray];
                    targets.Add(aim.Target);
                    doubling.Add(aim.Doubling);
                }

                AssignTargets(targets.AsArray(), doubling.AsArray(), candidates.AsArray());

                for (int k = start; k < end; k++)
                {
                    var aim = AimLookup[refs[k].Ray];
                    aim.Target = targets[k - start];
                    aim.Doubling = doubling[k - start];
                    aim.RetargetTimer = RetargetInterval;
                    aim.Served = true;
                    AimLookup[refs[k].Ray] = aim;
                }
            }

            for (int k = start; k < end; k++)
                UpdateRay(refs[k].Ray, p, n, range, in root);
        }

        private void CollectCandidates(float3 p, float3 n, float planetRadius, float range, uint hostileLayers,
            ref NativeList<Entity> candidates)
        {
            candidates.Clear();
            var filter = new CollisionFilter { BelongsTo = CollisionLayers.Raycast, CollidesWith = hostileLayers };
            var hits = new NativeList<DistanceHit>(32, Allocator.Temp);
            // A chord is never longer than its arc: the sphere of radius FinalRange holds every enemy within arc FinalRange.
            CollisionWorld.OverlapSphere(p, range, ref hits, filter);

            var seen = new NativeHashSet<Entity>(math.max(1, hits.Length), Allocator.Temp);
            var sorted = new NativeList<Candidate>(hits.Length, Allocator.Temp);
            for (int h = 0; h < hits.Length; h++)
            {
                Entity e = hits[h].Entity;
                if (!seen.Add(e))
                    continue; // a target may span several colliders
                if (DestroyLookup.HasComponent(e) && DestroyLookup.IsComponentEnabled(e))
                    continue;
                if (!TransformLookup.TryGetComponent(e, out var enemyTransform))
                    continue;

                float arc = planetRadius > 1e-4f
                    ? planetRadius * math.acos(math.clamp(math.dot(n, math.normalizesafe(enemyTransform.Position - PlanetCenter, n)), -1f, 1f))
                    : math.distance(p, enemyTransform.Position);
                if (arc > range)
                    continue;
                sorted.Add(new Candidate { Entity = e, Arc = arc });
            }
            sorted.Sort(new CandidateComparer());
            for (int i = 0; i < sorted.Length; i++)
                candidates.Add(sorted[i].Entity);

            sorted.Dispose();
            seen.Dispose();
            hits.Dispose();
        }

        private void UpdateRay(Entity ray, float3 p, float3 n, float range, in ActiveSpell root)
        {
            var aim = AimLookup[ray];
            var area = AreaLookup[ray];
            bool wasActive = ActiveLookup.IsComponentEnabled(ray);
            bool active = aim.Target != Entity.Null;

            // POWER from the ROOT, every frame (decision I2): birth, reconcile and mid-run upgrades alike; overwrites
            // UpdateTickDamageSpellsJob's partial refresh (this system runs after SpellStatsCalculationSystem).
            area.Damage = root.FinalDamage;
            area.CritChance = root.FinalCritChance;
            area.CritMultiplier = root.FinalCritDamageMultiplier;
            area.Tags = root.FinalTags;
            area.EffectsToApply = root.FinalEffects; // Beam_Ray carries no effects of its own
            area.TickRate = root.FinalTickRate;
            float baseRadius = area.PrefabRadius > 0f ? area.PrefabRadius : 1f;
            area.RadiusStart = root.FinalSize * baseRadius;
            area.RadiusEnd = area.RadiusStart;

            // Direction: re-projected on the current tangent plane (the caster moves on the sphere).
            float3 current = EmitterMath.TangentReference(n, aim.Direction);
            float3 dir = current;
            if (active && TransformLookup.TryGetComponent(aim.Target, out var targetTransform))
            {
                float3 desired = EmitterMath.TangentReference(n, targetTransform.Position - p);
                if (!wasActive || aim.TurnRateRadians <= 0f)
                {
                    dir = desired; // re-lit, or TurnRate ≤ 0 = instant follow: straight onto the target
                }
                else
                {
                    quaternion q = RotationMath.RotateTowards(
                        quaternion.LookRotationSafe(current, n),
                        quaternion.LookRotationSafe(desired, n),
                        aim.TurnRateRadians * DeltaTime);
                    dir = EmitterMath.TangentReference(n, math.forward(q));
                }
            }
            // Target lost between two passes (entity destroyed): keep the direction until the next pass (plan choice P4).
            aim.Direction = dir;

            if (active && !wasActive)
                area.ElapsedTime = area.TickRate; // first tick this frame (AreaAttackSystem runs after this system)

            var transform = TransformLookup[ray];
            transform.Position = p;
            transform.Rotation = quaternion.LookRotationSafe(dir, n);
            TransformLookup[ray] = transform;

            EmitterMath.LinePoint(p, n, dir, PlanetCenter, HasPlanet, range, out float3 b, out _, out _);
            SegmentLookup[ray] = new AreaSegment { A = p, B = b, Center = PlanetCenter, SurfaceArc = HasPlanet };

            AimLookup[ray] = aim;
            AreaLookup[ray] = area;
            if (active != wasActive)
                ActiveLookup.SetComponentEnabled(ray, active);
        }
    }
}
