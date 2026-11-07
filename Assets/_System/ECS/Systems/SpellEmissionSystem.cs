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

    /// <summary>None = always; Explode = allowed AND granted; Split = allowed AND FinalSplitCount > 0 AND depth left.</summary>
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
}

/// <summary>Drains the EmitRequest queue (posted through ECB the previous frame: children are born 2 frames after
/// their trigger, Q1). One single-threaded Burst job applies, per request: validity, active capability, Explode
/// Chance, count, per-lineage budget, global MaxEmitPerFrame (overflow dropped + counted, Q23), layout, then
/// spawns: emitted explosions through the shared low-level SpawnBurstArea (Q13 / K2), other children through
/// SpawnSpellInstance. Attached emitters are handled by Task 20's branch.</summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(SpellCastingSystem))]
[BurstCompile]
public partial struct SpellEmissionSystem : ISystem
{
    private NativeHashMap<Entity, LineageBudget> _lineageBudget;
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
        _spawn = SpellSpawnLookups.Create(ref state);
        _requestLookup = state.GetBufferLookup<EmitRequest>(false);
        _statsLookup = state.GetComponentLookup<EmitterStats>(false);
        _activeSpellLookup = state.GetBufferLookup<ActiveSpell>(true);
        _hitMemoryLookup = state.GetBufferLookup<HitEntityMemory>(true);
        _playerLookup = state.GetComponentLookup<Player>(true);
        _entityInfo = state.GetEntityStorageInfoLookup();
    }

    public void OnDestroy(ref SystemState state)
    {
        if (_lineageBudget.IsCreated)
            _lineageBudget.Dispose();
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

        var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
        float3 planetCenter = SystemAPI.TryGetSingleton<PlanetData>(out var planet) ? planet.Center : float3.zero;

        state.Dependency = new EmitJob
        {
            QueueEntity = queueEntity,
            RequestLookup = _requestLookup,
            StatsLookup = _statsLookup,
            Budget = _lineageBudget,
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
            Now = now,
            ClearAll = !hasPlayer,
            Purge = purge,
        }.Schedule(state.Dependency);
    }

    [BurstCompile]
    private struct EmitJob : IJob
    {
        private const float LineageIdleSeconds = 10f;

        public Entity QueueEntity;
        public BufferLookup<EmitRequest> RequestLookup;
        public ComponentLookup<EmitterStats> StatsLookup;
        public NativeHashMap<Entity, LineageBudget> Budget;
        public EntityCommandBuffer.ParallelWriter ECB;

        [ReadOnly] public BlobAssetReference<SpellBlobs> SpellDb;
        [ReadOnly] public DynamicBuffer<SpellPrefab> Prefabs;
        public bool PrefabsAligned;
        [ReadOnly] public SpellSpawnLookups Spawn;
        [ReadOnly] public BufferLookup<ActiveSpell> ActiveSpellLookup;
        [ReadOnly] public BufferLookup<HitEntityMemory> HitMemoryLookup;
        [ReadOnly] public ComponentLookup<Player> PlayerLookup;
        [ReadOnly] public EntityStorageInfoLookup EntityInfo;

        public EmitterConfig Config;
        public float3 PlanetCenter;
        public float Now;
        public bool ClearAll;
        public bool Purge;

        public void Execute()
        {
            var requests = RequestLookup[QueueEntity];
            if (ClearAll)
            {
                Budget.Clear();
                requests.Clear();
                return;
            }

            var stats = StatsLookup[QueueEntity];
            int emitted = 0;
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
                if (em.Attachment == EEmitterAttachment.Attached)
                    continue; // Task 20
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

                int count = ConsumeBudget(req.LineageId, requested, ref emitted, ref stats);
                if (count <= 0)
                    continue;

                SpawnDetached(in req, ref em, in root, ref rootBlob, ref spells[em.ChildIndex], Prefabs[em.ChildIndex].Prefab, count);
            }
        }

        private int ConsumeBudget(Entity lineageId, int requested, ref int emitted, ref EmitterStats stats)
        {
            Budget.TryGetValue(lineageId, out var entry);
            int byLineage = EmitterMath.ClampToBudget(requested, Config.LineageSpawnBudget - entry.Spent);
            stats.DroppedLineageBudget += requested - byLineage;
            int byFrame = EmitterMath.ClampToBudget(byLineage, Config.MaxEmitPerFrame - emitted);
            stats.DroppedFrameCap += byLineage - byFrame;

            entry.Spent += byFrame;
            entry.LastTouched = Now;
            Budget[lineageId] = entry;
            emitted += byFrame;
            return byFrame;
        }

        private void SpawnDetached(in EmitRequest req, ref EmitterBlob em, in ActiveSpell root, ref SpellBlob rootBlob,
            ref SpellBlob childBlob, Entity childPrefab, int count)
        {
            float3 normal = math.normalizesafe(req.Position - PlanetCenter, new float3(0f, 1f, 0f));
            float3 hint = math.lengthsq(req.Direction) > 1e-6f ? req.Direction : math.forward(req.Rotation);
            float3 dir = EmitterMath.TangentReference(normal, hint);
            quaternion baseRot = quaternion.LookRotationSafe(dir, normal);

            // Children never inherit the root's FinalEffects (Q5 / ruling K3): they carry the child SO's own
            // effects. Exception = attached None children, which read the root live (Task 20, ruling K4).
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
                Effects = EmitterQueries.CopyEffects(ref childBlob.Effects),
                TargetLayers = PlayerLookup.HasComponent(req.Caster)
                    ? CollisionLayers.Enemy | CollisionLayers.Obstacle
                    : CollisionLayers.Player | CollisionLayers.Obstacle,
            };
            var lineage = new SpellLineage
            {
                LineageId = req.LineageId, Generation = req.Generation, DepthLeft = req.DepthLeft, ShapeDatabaseIndex = em.ChildIndex,
            };
            var rules = new EmittedHitRules { CritMode = ECritMode.Roll, LifeStealBlocked = false };

            bool explode = em.Capability == ESpellCapability.Explode;
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
                p.Tags = root.FinalTags;      // ruling K3: the fragment IS the root spell (same family)
                p.Allowed = rootBlob.AllowedCapabilities;
                p.Bounces = root.FinalBounces;   // Q5: fragments inherit Bounce / Pierce
                p.BounceRange = root.FinalBounceRange;
                p.Pierces = root.FinalPierces;
                lineage.Generation = (byte)math.min(255, req.Generation + 1);
                lineage.DepthLeft = (byte)math.max(0, req.DepthLeft - 1);
            }
            else
            {
                p.Damage = root.FinalDamage;
                p.Scale = root.FinalSize;
                p.Tags = root.FinalTags;
                p.Allowed = ESpellCapability.None;
            }

            // Emitted explosions go through the shared low-level Burst-zone helper (Q13 / ruling K2): it writes
            // LocalToWorld itself, so the first BurstZoneJob tick reads the real position. The child's Lifetime
            // stays the prefab's (SpawnBurstArea does not write it), equal to the child SO by authoring.
            bool burstExplosion = explode && Spawn.AreaAttack.HasComponent(childPrefab);
            bool prefill = req.IgnoreTarget != Entity.Null && HitMemoryLookup.HasBuffer(childPrefab);

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

                Entity child;
                if (burstExplosion)
                {
                    child = SpellSpawnUtils.SpawnBurstArea(in Spawn.Transform, in Spawn.AreaAttack, in Spawn.SpellSource, ECB, 0,
                        childPrefab, pos, rot, p.Scale, p.Damage, p.CritChance, p.CritMultiplier, p.Tags, p.TargetLayers,
                        req.Caster, req.RootDatabaseIndex);
                }
                else
                {
                    p.Position = pos;
                    p.Rotation = rot;
                    p.Direction = d;
                    child = SpellSpawnUtils.SpawnSpellInstance(in Spawn, ECB, 0, in p);
                }

                ECB.AddComponent(0, child, lineage);
                ECB.AddComponent(0, child, rules);
                if (childBlob.ReHitDelayOverride > 0f)
                    ECB.AddComponent(0, child, new ReHitDelay { Value = childBlob.ReHitDelayOverride });
                if (prefill)
                    ECB.AppendToBuffer(0, child, new HitEntityMemory { HitEntity = req.IgnoreTarget, LastHitTime = 0, Pinned = true });
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
