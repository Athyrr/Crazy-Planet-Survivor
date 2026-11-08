using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

public struct LineageBudget
{
    public int Spent;
    public float LastTouched;
}

/// <summary>Blob/ActiveSpell queries shared by the emission posters and the drain.</summary>
public static class EmitterQueries
{
    public static bool HasTrigger(ref SpellBlob blob, EEmitterTrigger trigger)
    {
        for (int i = 0; i < blob.Emitters.Length; i++)
            if (blob.Emitters[i].Trigger == trigger)
                return true;
        return false;
    }

    /// <summary>None = always; Explode / Emit = allowed AND granted; Split = allowed AND FinalSplitCount > 0 AND depth left.</summary>
    public static bool IsEmitterActive(ref EmitterBlob em, in ActiveSpell root, ESpellCapability rootAllowed, byte depthLeft)
    {
        switch (em.Capability)
        {
            case ESpellCapability.None:
                return true;
            case ESpellCapability.Explode:
                return (rootAllowed & ESpellCapability.Explode) != 0 && (root.GrantedCapabilities & ESpellCapability.Explode) != 0;
            case ESpellCapability.Split:
                return (rootAllowed & ESpellCapability.Split) != 0 && root.FinalSplitCount > 0 && depthLeft > 0;
            case ESpellCapability.Emit:
                return (rootAllowed & ESpellCapability.Emit) != 0 && (root.GrantedCapabilities & ESpellCapability.Emit) != 0;
            default:
                return false;
        }
    }

    public static int ResolveCount(ref EmitterBlob em, in ActiveSpell root)
    {
        switch (em.Count)
        {
            case EEmitterCount.Fixed: return math.max(1, em.FixedCount);
            case EEmitterCount.Amount: return math.max(0, root.FinalAmount);
            case EEmitterCount.SplitCount: return math.max(0, root.FinalSplitCount);
            default: return 0;
        }
    }

    public static bool TryGetActiveSpell(BufferLookup<ActiveSpell> lookup, Entity caster, int dbIndex, out ActiveSpell spell)
    {
        spell = default;
        if (!lookup.TryGetBuffer(caster, out var spells))
            return false;
        for (int i = 0; i < spells.Length; i++)
        {
            if (spells[i].DatabaseIndex == dbIndex)
            {
                spell = spells[i];
                return true;
            }
        }
        return false;
    }

    public static FixedList32Bytes<EffectSpec> CopyEffects(ref BlobArray<EffectSpec> effects)
    {
        var list = new FixedList32Bytes<EffectSpec>();
        for (int i = 0; i < effects.Length && list.Length < list.Capacity; i++)
            list.Add(effects[i]);
        return list;
    }

    /// <summary>D11: child SO effects first, then the root's FinalEffects without a duplicate Type, within the list
    /// capacity (surplus silently ignored — theoretical: there are fewer effect types than slots).</summary>
    public static FixedList32Bytes<EffectSpec> MergeEffects(in FixedList32Bytes<EffectSpec> childEffects,
        in FixedList32Bytes<EffectSpec> rootEffects)
    {
        var list = childEffects;
        for (int i = 0; i < rootEffects.Length && list.Length < list.Capacity; i++)
        {
            bool duplicate = false;
            for (int j = 0; j < list.Length; j++)
            {
                if (list[j].Type == rootEffects[i].Type)
                {
                    duplicate = true;
                    break;
                }
            }
            if (!duplicate)
                list.Add(rootEffects[i]);
        }
        return list;
    }
}

/// <summary>Drains the EmitRequest queue (posted through ECB the previous frame: children are born 2 frames after
/// their trigger). One single-threaded Burst job applies, per request: validity, active capability, Explode
/// Chance, count, per-lineage budget, global MaxEmitPerFrame (overflow dropped + counted), layout (AtPoint / Circle /
/// Spread / Line), then spawns every detached child through the shared SpawnEmittedChild body (emitted explosions via
/// SpawnBurstArea, others via SpawnSpellInstance). None / Emit / Split children inherit the root FinalEffects (D11).
/// Sequential (None / Emit): child 0 spawns now, children i ≥ 1 become PendingEmit tickets processed by
/// ProcessTicketsJob before EmitJob.
/// Attached emitters (OnActivation only) set up the owner instance and spawn anchored
/// children with live root stats (AttachedEmitterUtils).</summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(SpellCastingSystem))]
[BurstCompile]
public partial struct SpellEmissionSystem : ISystem
{
    private NativeHashMap<Entity, LineageBudget> _lineageBudget;
    private NativeReference<int> _ticketsEmitted;
    private SpellSpawnLookups _spawn;
    private BufferLookup<EmitRequest> _requestLookup;
    private ComponentLookup<EmitterStats> _statsLookup;
    private BufferLookup<ActiveSpell> _activeSpellLookup;
    private BufferLookup<HitEntityMemory> _hitMemoryLookup;
    private ComponentLookup<Player> _playerLookup;
    private EntityStorageInfoLookup _entityInfo;
    private bool _warnedMissingConfig;
    private bool _warnedMisalignedPrefabs;
    private float _nextPurgeTime;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<SpellsDatabase>();
        state.RequireForUpdate<SpellPrefab>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();

        var queue = state.EntityManager.CreateEntity(typeof(EmitRequestQueue), typeof(EmitterStats));
        state.EntityManager.AddBuffer<EmitRequest>(queue);
        state.EntityManager.SetName(queue, "EmitRequestQueue");

        _lineageBudget = new NativeHashMap<Entity, LineageBudget>(256, Allocator.Persistent);
        _ticketsEmitted = new NativeReference<int>(0, Allocator.Persistent);
        _spawn = SpellSpawnLookups.Create(ref state);
        _requestLookup = state.GetBufferLookup<EmitRequest>(false);
        _statsLookup = state.GetComponentLookup<EmitterStats>(false);
        _activeSpellLookup = state.GetBufferLookup<ActiveSpell>(false); // written: PersistentInstance (attached owner)
        _hitMemoryLookup = state.GetBufferLookup<HitEntityMemory>(true);
        _playerLookup = state.GetComponentLookup<Player>(true);
        _entityInfo = state.GetEntityStorageInfoLookup();
    }

    public void OnDestroy(ref SystemState state)
    {
        if (_lineageBudget.IsCreated)
            _lineageBudget.Dispose();
        if (_ticketsEmitted.IsCreated)
            _ticketsEmitted.Dispose();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingletonEntity<EmitRequestQueue>(out var queueEntity))
            return;

        bool hasPlayer = SystemAPI.HasSingleton<Player>();
        bool running = SystemAPI.TryGetSingleton<GameState>(out var gameState) && gameState.State == EGameState.Running;
        if (hasPlayer && !running)
            return; // paused / upgrade selection: keep the queue for later

        EmitterConfig cfg;
        if (!SystemAPI.TryGetSingleton(out cfg))
        {
            cfg = EmitterConfig.Default;
            if (!_warnedMissingConfig)
            {
                _warnedMissingConfig = true;
                UnityEngine.Debug.LogWarning("[SpellEmissionSystem] EmitterConfig singleton missing: using EmitterConfig.Default (assign CpEmitterSettings on PF_ActiveEffectsRulesConfig and rebake SC_Entity_Core).");
            }
        }

        var spellDb = SystemAPI.GetSingleton<SpellsDatabase>().Blobs;
        var prefabs = SystemAPI.GetSingletonBuffer<SpellPrefab>(true);

        // SpellsDatabaseAuthoring skips a null SpellSO without adding its SpellPrefab, which shifts the prefab
        // buffer against the blob index used by EmitterBlob.ChildIndex. Equal lengths <=> no null entry.
        bool prefabsAligned = spellDb.IsCreated && prefabs.Length == spellDb.Value.Spells.Length;
        if (!prefabsAligned && !_warnedMisalignedPrefabs)
        {
            _warnedMisalignedPrefabs = true;
            UnityEngine.Debug.LogError("[SpellEmissionSystem] SpellPrefab buffer does not match SpellBlobs (null SpellSO in GameSpellsDatabase?): every emission is dropped. Remove the null entry and rebake SC_Entity_Core.");
        }

        float now = (float)SystemAPI.Time.ElapsedTime;
        bool purge = now >= _nextPurgeTime;
        if (purge)
            _nextPurgeTime = now + 1f;

        _spawn.Update(ref state);
        _requestLookup.Update(ref state);
        _statsLookup.Update(ref state);
        _activeSpellLookup.Update(ref state);
        _hitMemoryLookup.Update(ref state);
        _playerLookup.Update(ref state);
        _entityInfo.Update(ref state);

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        // One ECB per job (AreaAttackSystem convention): tickets and requests each get their own.
        var ticketEcb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);
        // PlanetCenter = float3.zero is a legitimate centre: the Line layout needs an explicit "no planet" flag.
        bool hasPlanet = SystemAPI.TryGetSingleton<PlanetData>(out var planet);
        float3 planetCenter = hasPlanet ? planet.Center : float3.zero;

        // Deferred children first: they are already paid on their lineage (spec §6). Pause / upgrade choice never
        // reach this point (early return above), so tickets freeze instead of bursting out on resume.
        state.Dependency = new ProcessTicketsJob
        {
            ECB = ticketEcb.AsParallelWriter(),
            Spawn = _spawn,
            TicketsEmitted = _ticketsEmitted,
            MaxEmitPerFrame = cfg.MaxEmitPerFrame,
            DeltaTime = SystemAPI.Time.DeltaTime,
            ClearAll = !hasPlayer,
        }.Schedule(state.Dependency);

        state.Dependency = new EmitJob
        {
            QueueEntity = queueEntity,
            RequestLookup = _requestLookup,
            StatsLookup = _statsLookup,
            Budget = _lineageBudget,
            TicketsEmitted = _ticketsEmitted,
            ECB = ecb.AsParallelWriter(),
            SpellDb = spellDb,
            Prefabs = prefabs,
            PrefabsAligned = prefabsAligned,
            Spawn = _spawn,
            ActiveSpellLookup = _activeSpellLookup,
            HitMemoryLookup = _hitMemoryLookup,
            PlayerLookup = _playerLookup,
            EntityInfo = _entityInfo,
            Config = cfg,
            PlanetCenter = planetCenter,
            HasPlanet = hasPlanet,
            Now = now,
            ClearAll = !hasPlayer,
            Purge = purge,
        }.Schedule(state.Dependency);
    }

    /// <summary>Single spawn body of a detached emitted child (position already resolved in p). Shared by the
    /// immediate spawn (EmitJob) and the deferred one (ProcessTicketsJob, Sequential): identical by construction.
    /// ignoreTarget = Null when nothing is pinned (or the prefab has no HitEntityMemory).</summary>
    private static void SpawnEmittedChild(in SpellSpawnLookups spawn, EntityCommandBuffer.ParallelWriter ecb, in SpellSpawnParams p,
        bool burstExplosion, in SpellLineage lineage, in EmittedHitRules rules, float reHitDelay, Entity ignoreTarget)
    {
        // Emitted explosions go through the shared low-level Burst-zone helper (it writes LocalToWorld itself; the
        // child's Lifetime stays the prefab's, equal to the child SO by authoring).
        Entity child = burstExplosion
            ? SpellSpawnUtils.SpawnBurstArea(in spawn.Transform, in spawn.AreaAttack, in spawn.SpellSource, ecb, 0,
                p.Prefab, p.Position, p.Rotation, p.Scale, p.Damage, p.CritChance, p.CritMultiplier, p.Tags, p.TargetLayers,
                p.Caster, p.DatabaseIndex, in p.Effects)
            : SpellSpawnUtils.SpawnSpellInstance(in spawn, ecb, 0, in p);

        ecb.AddComponent(0, child, lineage);
        ecb.AddComponent(0, child, rules);
        if (reHitDelay > 0f)
            ecb.AddComponent(0, child, new ReHitDelay { Value = reHitDelay });
        if (ignoreTarget != Entity.Null)
            ecb.AppendToBuffer(0, child, new HitEntityMemory { HitEntity = ignoreTarget, LastHitTime = 0, Pinned = true });
    }

    /// <summary>Spawns due PendingEmit tickets through the shared body, within MaxEmitPerFrame (a due ticket over the
    /// cap stays in the world and retries next frame: a deferral, not a drop). Single-threaded, before EmitJob,
    /// which reads then resets TicketsEmitted. ClearAll (no player) destroys every ticket.</summary>
    [BurstCompile]
    private partial struct ProcessTicketsJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;
        [ReadOnly] public SpellSpawnLookups Spawn;
        public NativeReference<int> TicketsEmitted;
        public int MaxEmitPerFrame;
        public float DeltaTime;
        public bool ClearAll;

        private void Execute(Entity entity, ref PendingEmit ticket)
        {
            if (ClearAll)
            {
                ECB.DestroyEntity(0, entity);
                return;
            }

            ticket.Remaining -= DeltaTime;
            if (ticket.Remaining > 0f)
                return;
            if (TicketsEmitted.Value >= MaxEmitPerFrame)
                return;

            SpawnEmittedChild(in Spawn, ECB, in ticket.Params, false, in ticket.Lineage, in ticket.Rules,
                ticket.ReHitDelay, ticket.IgnoreTarget);
            TicketsEmitted.Value = TicketsEmitted.Value + 1;
            ECB.DestroyEntity(0, entity);
        }
    }

    [BurstCompile]
    private struct EmitJob : IJob
    {
        private const float LineageIdleSeconds = 10f;

        public Entity QueueEntity;
        public BufferLookup<EmitRequest> RequestLookup;
        public ComponentLookup<EmitterStats> StatsLookup;
        public NativeHashMap<Entity, LineageBudget> Budget;
        public NativeReference<int> TicketsEmitted; // children spawned this frame by ProcessTicketsJob
        public EntityCommandBuffer.ParallelWriter ECB;

        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellDb;
        [ReadOnly] public DynamicBuffer<SpellPrefab> Prefabs;
        public bool PrefabsAligned;
        [ReadOnly] public SpellSpawnLookups Spawn;
        public BufferLookup<ActiveSpell> ActiveSpellLookup;
        [ReadOnly] public BufferLookup<HitEntityMemory> HitMemoryLookup;
        [ReadOnly] public ComponentLookup<Player> PlayerLookup;
        [ReadOnly] public EntityStorageInfoLookup EntityInfo;

        public EmitterConfig Config;
        public float3 PlanetCenter;
        public bool HasPlanet;
        public float Now;
        public bool ClearAll;
        public bool Purge;

        public void Execute()
        {
            var requests = RequestLookup[QueueEntity];

            // Tickets spawned earlier this frame count against MaxEmitPerFrame; read then reset (ClearAll included,
            // otherwise a run end would leave a stale count for the next run).
            int emitted = TicketsEmitted.Value;
            TicketsEmitted.Value = 0;

            if (ClearAll)
            {
                Budget.Clear();
                requests.Clear();
                return;
            }

            var stats = StatsLookup[QueueEntity];
            for (int r = 0; r < requests.Length; r++)
                Process(requests[r], ref emitted, ref stats);
            requests.Clear();

            if (Purge)
                PurgeIdle();

            stats.EmittedLastFrame = emitted;
            stats.PeakEmittedPerFrame = math.max(stats.PeakEmittedPerFrame, emitted);
            StatsLookup[QueueEntity] = stats;
        }

        private void Process(in EmitRequest req, ref int emitted, ref EmitterStats stats)
        {
            if (!SpellDb.IsCreated || !PrefabsAligned)
            {
                stats.DroppedInvalid++;
                return;
            }
            ref var spells = ref SpellDb.Value.Spells;
            if (req.RootDatabaseIndex < 0 || req.RootDatabaseIndex >= spells.Length
                || req.ShapeDatabaseIndex < 0 || req.ShapeDatabaseIndex >= spells.Length
                || !EntityInfo.Exists(req.Caster)
                || !EmitterQueries.TryGetActiveSpell(ActiveSpellLookup, req.Caster, req.RootDatabaseIndex, out ActiveSpell root))
            {
                stats.DroppedInvalid++;
                return;
            }

            ref var rootBlob = ref spells[req.RootDatabaseIndex];
            ref var shapeBlob = ref spells[req.ShapeDatabaseIndex];

            for (int slot = 0; slot < shapeBlob.Emitters.Length; slot++)
            {
                ref var em = ref shapeBlob.Emitters[slot];
                if (em.Trigger != req.Trigger)
                    continue;
                if (!EmitterQueries.IsEmitterActive(ref em, in root, rootBlob.AllowedCapabilities, req.DepthLeft))
                    continue;

                if (em.Capability == ESpellCapability.Explode && em.Chance < 1f)
                {
                    var rng = Random.CreateFromIndex(req.RollSeed ^ ((uint)(slot + 1) * 0x9E3779B1u));
                    if (!EmitterMath.ChanceRoll(rng.NextFloat(), em.Chance))
                        continue;
                }

                int requested = EmitterQueries.ResolveCount(ref em, in root);
                if (requested <= 0)
                    continue;

                if (em.ChildIndex < 0 || em.ChildIndex >= Prefabs.Length || Prefabs[em.ChildIndex].Prefab == Entity.Null)
                {
                    stats.DroppedInvalid += requested;
                    continue;
                }

                // Attached (after IsEmitterActive / Chance / requested / prefab check, before the
                // detached ConsumeBudget): children anchored to the owner instance, live root stats.
                if (em.Attachment == EEmitterAttachment.Attached)
                {
                    if (req.Trigger != EEmitterTrigger.OnActivation || req.Owner == Entity.Null || !EntityInfo.Exists(req.Owner))
                    {
                        stats.DroppedInvalid++;
                        continue;
                    }
                    int granted = ConsumeBudget(req.LineageId, requested, false, ref emitted, ref stats);
                    SpawnAttached(in req, ref em, in root, ref rootBlob, slot, Prefabs[em.ChildIndex].Prefab, granted);
                    continue;
                }

                // Sequential is reserved to None / Emit (D14, enforced at bake); anything else falls back to Instant.
                bool sequential = em.Timing == EEmitterTiming.Sequential
                    && (em.Capability == ESpellCapability.None || em.Capability == ESpellCapability.Emit);
                int count = ConsumeBudget(req.LineageId, requested, sequential, ref emitted, ref stats);
                if (count <= 0)
                    continue;

                SpawnDetached(in req, ref em, in root, ref rootBlob, ref spells[em.ChildIndex], Prefabs[em.ChildIndex].Prefab, count, sequential);
            }
        }

        /// <summary>Lineage budget: debited for every granted child at request time (deferred ones included).
        /// Frame cap: debited only for the children spawned THIS frame (all of them in Instant, child 0 in
        /// Sequential — a ticket pays the cap when it spawns). Sequential with no room for child 0 = the whole
        /// emission is dropped (DroppedFrameCap) without touching the lineage.</summary>
        private int ConsumeBudget(Entity lineageId, int requested, bool sequential, ref int emitted, ref EmitterStats stats)
        {
            Budget.TryGetValue(lineageId, out var entry);
            int byLineage = EmitterMath.ClampToBudget(requested, Config.LineageSpawnBudget - entry.Spent);
            stats.DroppedLineageBudget += requested - byLineage;

            int granted;
            int spawnedNow;
            if (sequential)
            {
                if (byLineage > 0 && Config.MaxEmitPerFrame - emitted < 1)
                {
                    stats.DroppedFrameCap += byLineage;
                    byLineage = 0;
                }
                granted = byLineage;
                spawnedNow = math.min(1, granted);
            }
            else
            {
                granted = EmitterMath.ClampToBudget(byLineage, Config.MaxEmitPerFrame - emitted);
                stats.DroppedFrameCap += byLineage - granted;
                spawnedNow = granted;
            }

            entry.Spent += granted;
            entry.LastTouched = Now;
            Budget[lineageId] = entry;
            emitted += spawnedNow;
            return granted;
        }

        private void SpawnAttached(in EmitRequest req, ref EmitterBlob em, in ActiveSpell root, ref SpellBlob rootBlob,
            int slot, Entity childPrefab, int count)
        {
            float childBaseScale = SpellSpawnUtils.PrefabScale(in Spawn.Transform, childPrefab);
            ECB.SetComponent(0, req.Owner, new EmitterOwner
            {
                Angle = 0f,
                AngularSpeed = math.radians(root.FinalSpeed),
                Count = count,
                Radius = EmitterMath.OrbitRadius(em.Radius, root.FinalRange, rootBlob.BaseCastRange),
                Frame = em.Frame,
                EmitterIndex = slot,
                ChildBaseScale = childBaseScale,
                ChildScale = childBaseScale * root.FinalSize,
            });

            uint layers = PlayerLookup.HasComponent(req.Caster)
                ? CollisionLayers.Enemy | CollisionLayers.Obstacle
                : CollisionLayers.Player | CollisionLayers.Obstacle;
            for (int i = 0; i < count; i++)
                AttachedEmitterUtils.SpawnAnchoredChild(in Spawn, ECB, 0, req.Owner, childPrefab, i, req.Caster,
                    req.RootDatabaseIndex, req.Position, layers);

            // Lifetime = -1 (read from the blob, not FinalDuration which clamps to 0.1): single persistent instance.
            if (rootBlob.Lifetime < 0f && ActiveSpellLookup.TryGetBuffer(req.Caster, out var spells))
            {
                for (int k = 0; k < spells.Length; k++)
                {
                    if (spells[k].DatabaseIndex != req.RootDatabaseIndex) continue;
                    var s = spells[k];
                    s.PersistentInstance = req.Owner;
                    spells[k] = s;
                    break;
                }
            }
        }

        private void SpawnDetached(in EmitRequest req, ref EmitterBlob em, in ActiveSpell root, ref SpellBlob rootBlob,
            ref SpellBlob childBlob, Entity childPrefab, int count, bool sequential)
        {
            float3 normal = math.normalizesafe(req.Position - PlanetCenter, new float3(0f, 1f, 0f));
            float3 hint = math.lengthsq(req.Direction) > 1e-6f ? req.Direction : math.forward(req.Rotation);
            float3 dir = EmitterMath.TangentReference(normal, hint);
            quaternion baseRot = quaternion.LookRotationSafe(dir, normal);

            // Effects (D11): None / Emit / Split children carry the child SO's effects plus the root's FinalEffects
            // (upgrades included). Explode keeps the child SO's effects only (lot 1 ruling). Attached None children
            // still read the root live (AttachedEmitterUtils).
            bool explode = em.Capability == ESpellCapability.Explode;
            var childEffects = EmitterQueries.CopyEffects(ref childBlob.Effects);
            var p = new SpellSpawnParams
            {
                Prefab = childPrefab,
                Caster = req.Caster,
                DatabaseIndex = req.RootDatabaseIndex,
                Target = Entity.Null,
                CasterOffset = childBlob.BaseSpawnOffset,
                Speed = childBlob.BaseSpeed,
                Duration = childBlob.Lifetime,
                TickRate = childBlob.TickRate,
                CritChance = root.FinalCritChance,
                CritMultiplier = root.FinalCritDamageMultiplier,
                Effects = explode ? childEffects : EmitterQueries.MergeEffects(in childEffects, in root.FinalEffects),
                TargetLayers = PlayerLookup.HasComponent(req.Caster)
                    ? CollisionLayers.Enemy | CollisionLayers.Obstacle
                    : CollisionLayers.Player | CollisionLayers.Obstacle,
            };
            var lineage = new SpellLineage
            {
                LineageId = req.LineageId, Generation = req.Generation, DepthLeft = req.DepthLeft, ShapeDatabaseIndex = em.ChildIndex,
            };
            var rules = new EmittedHitRules { CritMode = ECritMode.Roll, LifeStealBlocked = false };

            if (explode)
            {
                float ratio = em.DamageRatio > 0f ? em.DamageRatio : Config.ExplosionDamageRatio;
                p.Damage = root.FinalDamage * ratio;
                p.Scale = EmitterMath.ExplosionScale(root.FinalExplosionSize);
                p.Tags = childBlob.Tag | ESpellTag.Explosive;
                p.Allowed = ESpellCapability.None;
                rules.CritMode = req.TriggerCrit ? ECritMode.ForceCrit : ECritMode.ForceNoCrit;
                rules.LifeStealBlocked = true;
            }
            else if (em.Capability == ESpellCapability.Split)
            {
                p.Damage = root.FinalDamage; // falloff^generation is applied at hit time (ResolveHit)
                p.Scale = root.FinalSize;
                p.Tags = root.FinalTags;      // the fragment IS the root spell (same family)
                p.Allowed = rootBlob.AllowedCapabilities;
                p.Bounces = root.FinalBounces;   // fragments inherit Bounce / Pierce
                p.BounceRange = root.FinalBounceRange;
                p.Pierces = root.FinalPierces;
                lineage.Generation = (byte)math.min(255, req.Generation + 1);
                lineage.DepthLeft = (byte)math.max(0, req.DepthLeft - 1);
            }
            else
            {
                // None and Emit: root power. Emit scales by its own DamageRatio (D13: 0 = 1.0, full root damage).
                float ratio = em.Capability == ESpellCapability.Emit && em.DamageRatio > 0f ? em.DamageRatio : 1f;
                p.Damage = root.FinalDamage * ratio;
                p.Scale = root.FinalSize;
                p.Tags = root.FinalTags;
                p.Allowed = ESpellCapability.None;
            }

            bool burstExplosion = explode && Spawn.AreaAttack.HasComponent(childPrefab);
            Entity ignoreTarget = req.IgnoreTarget != Entity.Null && HitMemoryLookup.HasBuffer(childPrefab)
                ? req.IgnoreTarget
                : Entity.Null;
            float reHitDelay = childBlob.ReHitDelayOverride;

            for (int i = 0; i < count; i++)
            {
                float3 pos = req.Position;
                quaternion rot = baseRot;
                float3 d = dir;

                if (em.Layout == EEmitterLayout.Spread)
                {
                    float angle = math.radians(EmitterMath.SpreadAngleDegrees(i, count, em.SpreadAngleDegrees));
                    rot = math.mul(quaternion.AxisAngle(normal, angle), baseRot);
                    d = math.forward(rot);
                }
                else if (em.Layout == EEmitterLayout.Circle)
                {
                    float radius = EmitterMath.OrbitRadius(em.Radius, root.FinalRange, rootBlob.BaseCastRange);
                    float3 offset = EmitterMath.OrbitOffset(normal, dir, radius, EmitterMath.CircleAngleRadians(0f, i, count));
                    pos = req.Position + offset;
                    d = math.normalizesafe(offset, dir);
                    rot = quaternion.LookRotationSafe(d, normal);
                }
                else if (em.Layout == EEmitterLayout.Line)
                {
                    // Arc along dir (unit and tangent by TangentReference, as LinePoint requires). D4: fixed spacing
                    // × FinalSize; Radius / SpreadAngle ignored.
                    float distance = (em.LineStartDistance + i * em.LineSpacing) * root.FinalSize;
                    EmitterMath.LinePoint(req.Position, normal, dir, PlanetCenter, HasPlanet, distance, out pos, out d, out float3 up);
                    rot = quaternion.LookRotationSafe(d, up);
                }

                p.Position = pos;
                p.Rotation = rot;
                p.Direction = d;

                if (sequential && i > 0)
                {
                    // Deferred child (D9): a ticket frozen with everything the immediate spawn would write.
                    Entity ticket = ECB.CreateEntity(0);
                    ECB.AddComponent(0, ticket, new PendingEmit
                    {
                        Params = p,
                        Lineage = lineage,
                        Rules = rules,
                        ReHitDelay = reHitDelay,
                        IgnoreTarget = ignoreTarget,
                        Remaining = i * em.SequenceInterval,
                    });
                    continue;
                }

                SpawnEmittedChild(in Spawn, ECB, in p, burstExplosion, in lineage, in rules, reHitDelay, ignoreTarget);
            }
        }

        private void PurgeIdle()
        {
            var keys = Budget.GetKeyArray(Allocator.Temp);
            for (int i = 0; i < keys.Length; i++)
                if (Budget.TryGetValue(keys[i], out var entry) && Now - entry.LastTouched > LineageIdleSeconds)
                    Budget.Remove(keys[i]);
            keys.Dispose();
        }
    }
}
