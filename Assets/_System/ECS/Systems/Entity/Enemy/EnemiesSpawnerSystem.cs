using Unity.Collections;
using Unity.Mathematics;
using Unity.Transforms;
using Unity.Entities;
using Unity.Physics;
using Unity.Burst;
using Unity.Jobs;

/// <summary>
/// Handles the logic for spawning enemies in waves on a spherical planet surface.
/// This system manages wave timing, processes pending spawn queues across multiple frames to prevent 
/// performance spikes, and calculates spawn positions based on various geometric modes.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(PlayerSpawnerSystem))]
[BurstCompile]
public partial struct EnemiesSpawnerSystem : ISystem
{
    // Queries
    //private EntityQuery _playerQuery;

    /// <summary>
    /// Limits the number of entities instantiated in a single frame to maintain a stable frame rate.
    /// </summary>
    private const int MAX_SPAWNS_PER_FRAME = 50;

    /// <summary>
    /// Minimum seconds between two iterations of a looping wave. Floors the loop period and gates the
    /// kill-% restart, so straggler kills from a previous iteration can't make a loop restart every frame.
    /// </summary>
    private const float MIN_LOOP_PERIOD = 0.5f;

    //todo allocate Ms budget et dispacth into frames

    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PhysicsWorldSingleton>();
        state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        //state.RequireForUpdate<StartRunRequest>();
        state.RequireForUpdate<SpawnerSettings>();
        state.RequireForUpdate<SpawnerState>();
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<Player>();

        //_playerQuery = state.GetEntityQuery(ComponentType.ReadOnly<Player>());
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out var gameState) || gameState.State != EGameState.Running)
            return;

        //if (_playerQuery.IsEmpty)
        //    return;

        ref var spawnerState = ref SystemAPI.GetSingletonRW<SpawnerState>().ValueRW;
        DynamicBuffer<Wave> waves = SystemAPI.GetSingletonBuffer<Wave>(true);
        DynamicBuffer<WaveRuntime> waveRuntimes = SystemAPI.GetSingletonBuffer<WaveRuntime>(false);
        DynamicBuffer<SpawnGroup> groups = SystemAPI.GetSingletonBuffer<SpawnGroup>(true);
        DynamicBuffer<SpawnGroupRuntime> groupRuntimes = SystemAPI.GetSingletonBuffer<SpawnGroupRuntime>(false);
        SpawnerSettings settings = SystemAPI.GetSingleton<SpawnerSettings>();


        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        // Handle kills (attributed per wave)
        ProcessKills(ref ecb, ref state, ref spawnerState, waveRuntimes);
        // Handle timers, wave progression and loop restarts
        ManageWaveProgression(ref state, ref spawnerState, waves, groups, waveRuntimes, groupRuntimes);
        // Handle Spawning across every active wave
        ManageSpawning(ref ecb, ref state, ref spawnerState, waves, groups, waveRuntimes, groupRuntimes,
            settings.MaxEnemies);
    }

    private void ProcessKills(ref EntityCommandBuffer ecb, ref SystemState state, ref SpawnerState spawnerState,
        DynamicBuffer<WaveRuntime> waveRuntimes)
    {
        foreach (var (evt, entity) in SystemAPI.Query<RefRO<EnemyKilledEvent>>().WithEntityAccess())
        {
            // Credit the kill to its own wave (a wave may be a background loop, not the lead).
            int wi = evt.ValueRO.WaveIndex;
            if (wi >= 0 && wi < waveRuntimes.Length)
            {
                var rt = waveRuntimes[wi];
                rt.KilledCount++;
                waveRuntimes[wi] = rt;
            }

            spawnerState.ActiveEnemyCount--;
            if (spawnerState.ActiveEnemyCount < 0)
                spawnerState.ActiveEnemyCount = 0;

            ecb.DestroyEntity(entity);
        }
    }

    /// <summary>
    /// Ticks every active wave's timer and applies completion. Background loop waves (already passed by
    /// the lead) re-arm themselves in place. The lead wave advances the sequential frontier to the next
    /// wave and, if it loops, re-arms itself so it keeps running in parallel behind the new lead.
    /// </summary>
    private void ManageWaveProgression(ref SystemState state, ref SpawnerState spawnerState, DynamicBuffer<Wave> waves,
        DynamicBuffer<SpawnGroup> groups, DynamicBuffer<WaveRuntime> waveRuntimes,
        DynamicBuffer<SpawnGroupRuntime> groupRuntimes)
    {
        if (waves.Length == 0)
            return; // dirty condition, but never mind

        // First update of a run: clear any stale loop state, then arm wave 0 as the lead.
        if (spawnerState.CurrentWaveIndex == -1)
        {
            for (int i = 0; i < waveRuntimes.Length; i++)
                waveRuntimes[i] = default; // Active = false

            spawnerState.CurrentWaveIndex = 0;
            ArmWave(waves, groups, waveRuntimes, groupRuntimes, 0);
            return;
        }

        float dt = SystemAPI.Time.DeltaTime;
        int lead = spawnerState.CurrentWaveIndex;

        // 1) Background loop waves (strictly behind the lead, so necessarily loop waves): re-arm on completion.
        for (int i = 0; i < lead; i++)
        {
            var rt = waveRuntimes[i];
            if (!rt.Active)
                continue;

            rt.Timer -= dt;
            waveRuntimes[i] = rt;

            if (ShouldLoopRestart(waves[i], rt))
                ArmWave(waves, groups, waveRuntimes, groupRuntimes, i);
        }

        // 2) Lead wave: drives sequential progression.
        var leadRt = waveRuntimes[lead];
        if (!leadRt.Active)
            return;

        leadRt.Timer -= dt;
        waveRuntimes[lead] = leadRt;

        Wave leadWave = waves[lead];
        bool hasNext = lead + 1 < waves.Length;

        if (hasNext)
        {
            // Sequential advancement on (timeout || kill %) — ungated, as normal wave progression.
            if (!ConditionMet(leadWave, leadRt))
                return;

            spawnerState.CurrentWaveIndex = lead + 1;
            ArmWave(waves, groups, waveRuntimes, groupRuntimes, lead + 1);

            if (leadWave.Loop)
            {
                // Keep this wave running in parallel behind the new lead.
                ArmWave(waves, groups, waveRuntimes, groupRuntimes, lead);
            }
            else
            {
                // A non-loop wave that has been passed stops spawning its remainder (matches prior behavior).
                var done = waveRuntimes[lead];
                done.Active = false;
                waveRuntimes[lead] = done;
            }
        }
        else if (leadWave.Loop && ShouldLoopRestart(leadWave, leadRt))
        {
            // Last wave, no successor to advance to: self-restart on the gated loop condition so a fast
            // kill clear can't restart it every frame.
            ArmWave(waves, groups, waveRuntimes, groupRuntimes, lead);
        }
        // else: last, non-loop wave -> stays active so its groups finish spawning, then idles.
    }

    /// <summary> Lead-advance / first-iteration completion: timeout or kill-% reached (no min-period gate). </summary>
    private static bool ConditionMet(in Wave wave, in WaveRuntime rt)
    {
        return rt.Timer <= 0f || KillReached(wave, rt);
    }

    /// <summary>
    /// Background loop restart: a full (floored) period elapsed, or kill-% reached but only after
    /// MIN_LOOP_PERIOD of the current iteration, so leftover kills can't restart the loop every frame.
    /// </summary>
    private static bool ShouldLoopRestart(in Wave wave, in WaveRuntime rt)
    {
        if (rt.Timer <= 0f)
            return true;

        if (!KillReached(wave, rt))
            return false;

        float elapsed = LoopPeriod(wave) - rt.Timer;
        return elapsed >= MIN_LOOP_PERIOD;
    }

    /// <summary> Kill-% condition. A KillPercentage of 0 (or no enemies) means the condition is disabled. </summary>
    private static bool KillReached(in Wave wave, in WaveRuntime rt)
    {
        if (wave.TotalEnemyCount <= 0 || wave.KillPercentage <= 0f)
            return false;

        return (float)rt.KilledCount / wave.TotalEnemyCount >= wave.KillPercentage;
    }

    /// <summary> A loop wave's iteration length, floored so a tiny Duration can't restart it every frame. </summary>
    private static float LoopPeriod(in Wave wave)
    {
        return math.max(wave.Duration, MIN_LOOP_PERIOD);
    }

    /// <summary>
    /// Arms (or re-arms) a wave: marks its runtime Active, resets its iteration timer/kill count, and
    /// re-initializes its group range so every group starts popcorning from its first enemy.
    /// </summary>
    private void ArmWave(DynamicBuffer<Wave> waves, DynamicBuffer<SpawnGroup> groups,
        DynamicBuffer<WaveRuntime> waveRuntimes, DynamicBuffer<SpawnGroupRuntime> groupRuntimes, int index)
    {
        Wave wave = waves[index];

        waveRuntimes[index] = new WaveRuntime
        {
            Active = true,
            Timer = wave.Loop ? LoopPeriod(wave) : wave.Duration,
            KilledCount = 0
        };

        int endGroupIndex = wave.GroupStartIndex + wave.GroupCount;
        for (int gi = wave.GroupStartIndex; gi < endGroupIndex; gi++)
        {
            groupRuntimes[gi] = new SpawnGroupRuntime
            {
                Remaining = groups[gi].Amount,
                SpawnTimer = 0f
            };
        }
    }

    /// <summary>
    /// Spawns from every active wave (the lead wave plus any looping waves behind it). Each group still
    /// "popcorns" its enemies one every <see cref="SpawnGroup.SpawnDelay"/> seconds (delay &lt;= 0 dumps
    /// the whole group). A single per-frame budget (frame-rate + hard enemy cap) and a single monotonic
    /// ECB sort-key range are shared across all waves; the lead wave is served first so background loops
    /// can't starve it. Spawns the budget can't fit are deferred, never dropped.
    /// </summary>
    private void ManageSpawning(ref EntityCommandBuffer ecb, ref SystemState systemState, ref SpawnerState spawnerState,
        DynamicBuffer<Wave> waves, DynamicBuffer<SpawnGroup> groups, DynamicBuffer<WaveRuntime> waveRuntimes,
        DynamicBuffer<SpawnGroupRuntime> groupRuntimes, int maxEnemies)
    {
        int lead = spawnerState.CurrentWaveIndex;
        if (lead < 0)
            return;

        // Global per-frame safety budget shared by every active wave (frame-rate spike guard + hard cap).
        int frameBudget = math.min(MAX_SPAWNS_PER_FRAME, maxEnemies - spawnerState.ActiveEnemyCount);
        if (frameBudget <= 0)
            return;

        // The player may be mid-respawn this frame; bail rather than letting GetSingletonEntity throw.
        if (!SystemAPI.TryGetSingletonEntity<Player>(out var playerEntity))
            return;

        // Fetch the frame-invariant spawn inputs ONCE (this would otherwise run per active group).
        var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
        var planetEntity = SystemAPI.GetSingletonEntity<PlanetData>();
        var planetData = SystemAPI.GetComponentRO<PlanetData>(planetEntity).ValueRO;
        var planetTransform = SystemAPI.GetComponentRO<LocalTransform>(planetEntity).ValueRO;
        var playerTransform = SystemAPI.GetComponentRO<LocalTransform>(playerEntity).ValueRO;

        var ctx = new SpawnContext
        {
            CollisionWorld = physicsWorld.CollisionWorld,
            PlayerTransform = playerTransform,
            PlanetCenter = planetTransform.Position,
            PlanetRadius = planetData.Radius,
            BaseSeed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + 1
        };

        float dt = SystemAPI.Time.DeltaTime;
        // One monotonic sort-key range across ALL waves spawned this frame so their ECB commands never collide.
        int sortKeyBase = 0;

        // Lead wave first so the current wave is never starved by background loops...
        if (waveRuntimes[lead].Active)
            SpawnWaveGroups(ref ecb, ref systemState, ref spawnerState, groups, groupRuntimes, waves[lead], lead,
                dt, in ctx, ref frameBudget, ref sortKeyBase);

        // ...then the looping waves behind it.
        for (int wi = 0; wi < lead && frameBudget > 0; wi++)
        {
            if (waveRuntimes[wi].Active)
                SpawnWaveGroups(ref ecb, ref systemState, ref spawnerState, groups, groupRuntimes, waves[wi], wi,
                    dt, in ctx, ref frameBudget, ref sortKeyBase);
        }
    }

    /// <summary>
    /// Spawns from one wave's group range, consuming the shared frame budget and advancing the shared
    /// sort-key base. See <see cref="ManageSpawning"/> for the popcorn/budget contract.
    /// </summary>
    private void SpawnWaveGroups(ref EntityCommandBuffer ecb, ref SystemState systemState,
        ref SpawnerState spawnerState, DynamicBuffer<SpawnGroup> groups, DynamicBuffer<SpawnGroupRuntime> groupRuntimes,
        in Wave wave, int waveIndex, float dt, in SpawnContext ctx, ref int frameBudget, ref int sortKeyBase)
    {
        int endGroupIndex = wave.GroupStartIndex + wave.GroupCount;

        for (int gi = wave.GroupStartIndex; gi < endGroupIndex && frameBudget > 0; gi++)
        {
            var runtime = groupRuntimes[gi];
            if (runtime.Remaining <= 0)
                continue; // group finished this iteration

            var group = groups[gi];

            // Cap by both the group's remaining count and the shared frame budget.
            int allowed = math.min(frameBudget, runtime.Remaining);
            int countToSpawn;

            if (group.SpawnDelay <= 0f)
            {
                // No inter-spawn delay: release as many as the budget allows this frame.
                countToSpawn = allowed;
            }
            else
            {
                // Popcorn: tick the timer and release one enemy per elapsed delay (catch-up included).
                // If the budget caps us below what time owes, the timer stays negative and carries the
                // backlog to later frames.
                runtime.SpawnTimer -= dt;
                countToSpawn = 0;
                while (runtime.SpawnTimer <= 0f && countToSpawn < allowed)
                {
                    countToSpawn++;
                    runtime.SpawnTimer += group.SpawnDelay;
                }
            }

            if (countToSpawn > 0)
            {
                // Enemies already released in this group -> preserves the geometric layout across batches.
                int startIndex = group.Amount - runtime.Remaining;
                ScheduleSpawnJob(ref ecb, ref systemState, in ctx, group, startIndex, countToSpawn, waveIndex,
                    sortKeyBase);

                runtime.Remaining -= countToSpawn;
                spawnerState.ActiveEnemyCount += countToSpawn;

                frameBudget -= countToSpawn;
                sortKeyBase += countToSpawn;
            }

            groupRuntimes[gi] = runtime;
        }
    }

    private void ScheduleSpawnJob(ref EntityCommandBuffer ecb, ref SystemState state, in SpawnContext ctx,
        SpawnGroup group, int startIndex, int count, int waveIndex, int sortKeyBase)
    {
        var spawnJob = new SpawnJob
        {
            ECB = ecb.AsParallelWriter(),

            CollisionWorld = ctx.CollisionWorld,

            PlayerTransform = ctx.PlayerTransform,
            Prefab = group.Prefab,

            TotalAmount = group.Amount,
            StartIndex = startIndex,
            Mode = group.Mode,
            WaveIndex = waveIndex,
            SortKeyBase = sortKeyBase,
            // Scale = group.Scale, // issue ? @todo

            PlanetCenter = ctx.PlanetCenter,
            PlanetRadius = ctx.PlanetRadius,

            SpawnOrigin = group.Position,
            MinRange = group.MinRange,
            MaxRange = group.MaxRange,

            BaseSeed = ctx.BaseSeed
        };

        // Chained on state.Dependency so the shared ECB.ParallelWriter is written by one job at a time
        // (concurrent jobs on one ParallelWriter would race its per-thread command buffers).
        state.Dependency = spawnJob.ScheduleParallel(count, 64, state.Dependency);
    }

    /// <summary> Frame-invariant inputs for spawning, fetched once per frame and shared by every wave/group. </summary>
    private struct SpawnContext
    {
        public CollisionWorld CollisionWorld;
        public LocalTransform PlayerTransform;
        public float3 PlanetCenter;
        public float PlanetRadius;

        /// <summary> Frame-stable base; combined with the frame-global sort key for a unique per-entity seed. </summary>
        public uint BaseSeed;
    }

    [BurstCompile]
    private struct SpawnJob : IJobFor
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        [ReadOnly] public CollisionWorld CollisionWorld;

        [ReadOnly] public LocalTransform PlayerTransform;
        [ReadOnly] public Entity Prefab;

        // Spawn Configuration
        public int TotalAmount;
        public int StartIndex;
        public SpawnMode Mode;
        public int WaveIndex;
        public float Scale;

        /// <summary> Base offset added to every ECB sort key so concurrent group jobs stay disjoint. </summary>
        public int SortKeyBase;

        // Planet Data
        public float3 PlanetCenter;
        public float PlanetRadius;

        // Spawn Parameters
        public float3 SpawnOrigin;
        public float MinRange;
        public float MaxRange;

        // Random
        public uint BaseSeed;

        public void Execute(int index)
        {
            int globalIndex = StartIndex + index;
            // Frame-global unique seed: SortKeyBase + index never collides across waves/groups this frame.
            var rand = Random.CreateFromIndex(BaseSeed + (uint)(SortKeyBase + index));

            float3 spawnPosition = float3.zero;
            float3 surfaceNormal = float3.zero;
            bool positionFound = false;

            var groundFilter = new CollisionFilter
            {
                BelongsTo = CollisionLayers.Raycast,
                CollidesWith = CollisionLayers.Landscape
            };

            // Calculate spawn postion based on spawning mode
            switch (Mode)
            {
                // case SpawnMode.RandomInPlanet:
                //     float3 randomDir = rand.NextFloat3Direction();
                //     float3 roughPos = PlanetCenter + randomDir * PlanetRadius;
                //
                //     if (PlanetUtils.SnapToSurfaceRaycast(ref CollisionWorld, roughPos, PlanetCenter, groundFilter, 150f,
                //             out var hit))
                //     {
                //         spawnPosition = hit.Position;
                //         surfaceNormal = hit.SurfaceNormal;
                //         positionFound = true;
                //     }
                //
                //     break;

                case SpawnMode.RandomInPlanet:
                    float goldenAngleSphere = 2.39996323f; // PI * (3 - sqrt(5))

                    float maxAmount = math.max(1f, (float)TotalAmount);
                    float z = 1f - (2f * globalIndex + 1f) / maxAmount;

                    float radiusAtZ = math.sqrt(1f - z * z);

                    float thetaSphere = goldenAngleSphere * globalIndex;

                    float x = radiusAtZ * math.cos(thetaSphere);
                    float y = radiusAtZ * math.sin(thetaSphere);

                    float3 sphereDirection = new float3(x, y, z);
                    float3 roughPosPlanet = PlanetCenter + (sphereDirection * PlanetRadius);

                    if (PlanetUtils.SnapToSurfaceRaycast(ref CollisionWorld, roughPosPlanet, PlanetCenter, groundFilter,
                            150f, out var hitPlanet))
                    {
                        spawnPosition = hitPlanet.Position;
                        surfaceNormal = hitPlanet.SurfaceNormal;
                        positionFound = true;
                    }

                    break;

                // case SpawnMode.Zone:
                //     float zoneRadius = math.max(5f, TotalAmount * 0.5f);
                //     positionFound = PlanetUtils.GetRandomPointOnSurface(
                //         ref CollisionWorld, ref rand, SpawnOrigin, PlanetCenter, zoneRadius, ref groundFilter,
                //         out spawnPosition, out surfaceNormal);
                //     break;

                case SpawnMode.Zone:
                    float goldenAngle = 2.39996323f;

                    float c = MaxRange / math.sqrt(TotalAmount);

                    float angle = globalIndex * goldenAngle;
                    float spiralRadius = c * math.sqrt(globalIndex);

                    spiralRadius += MinRange;

                    float2 perfectCircle = new float2(math.cos(angle), math.sin(angle)) * spiralRadius;

                    float3 up = math.normalize(SpawnOrigin - PlanetCenter);
                    float3 tangent = math.cross(up, new float3(0, 1, 0));
                    if (math.lengthsq(tangent) < 0.001f)
                        tangent = math.cross(up, new float3(1, 0, 0));

                    quaternion alignmentRot = quaternion.LookRotationSafe(tangent, up);
                    float3 localOffset = new float3(perfectCircle.x, 0f, perfectCircle.y);
                    float3 worldOffset = math.rotate(alignmentRot, localOffset);
                    float3 p = SpawnOrigin + worldOffset;

                    if (PlanetUtils.SnapToSurfaceRaycast(ref CollisionWorld, p, PlanetCenter, groundFilter, 100f,
                            out var h))
                    {
                        spawnPosition = h.Position;
                        surfaceNormal = h.SurfaceNormal;
                        positionFound = true;
                    }

                    break;

                case SpawnMode.PlayerOpposite:
                    // Opposite point from player
                    float3 playerPosition = PlayerTransform.Position;
                    float3 dirToOrigin = math.normalize(playerPosition - PlanetCenter);
                    float3 opositePoint = PlanetCenter - (dirToOrigin * PlanetRadius * 1f);

                    // Avoid stacking
                    float oppositePositionRadius = math.max(15f, TotalAmount * 0.5f);

                    positionFound = PlanetUtils.GetRandomPointOnSurface(
                        ref CollisionWorld, ref rand, opositePoint, PlanetCenter, oppositePositionRadius,
                        ref groundFilter,
                        out spawnPosition, out surfaceNormal);
                    break;

                // case SpawnMode.AroundPlayer:
                //     positionFound = PlanetUtils.GetRandomPointOnSurface(
                //         ref CollisionWorld, ref rand, SpawnOrigin, PlanetCenter, MinRange, MaxRange, ref groundFilter,
                //         out spawnPosition, out surfaceNormal);
                //     break;

                case SpawnMode.AroundPlayer:
                    float goldenAngleAround = 2.39996323f;
                    float angleAround = globalIndex * goldenAngleAround;

                    float fraction = (float)globalIndex / TotalAmount;

                    float radiusAround = math.lerp(MinRange, MaxRange, math.sqrt(fraction));

                    float2 perfectCircleAround =
                        new float2(math.cos(angleAround), math.sin(angleAround)) * radiusAround;

                    // AroundPlayer centers the spawn ring on the player (not the group origin).
                    float3 centerAround = PlayerTransform.Position;
                    float3 upAround = math.normalize(centerAround - PlanetCenter);
                    float3 tangentAround = math.cross(upAround, new float3(0, 1, 0));
                    if (math.lengthsq(tangentAround) < 0.001f)
                        tangentAround = math.cross(upAround, new float3(1, 0, 0));

                    quaternion alignmentRotAround = quaternion.LookRotationSafe(tangentAround, upAround);
                    float3 localOffsetAround = new float3(perfectCircleAround.x, 0f, perfectCircleAround.y);
                    float3 worldOffsetAround = math.rotate(alignmentRotAround, localOffsetAround);
                    float3 roughPosAround = centerAround + worldOffsetAround;

                    if (PlanetUtils.SnapToSurfaceRaycast(ref CollisionWorld, roughPosAround, PlanetCenter, groundFilter,
                            100f, out var hitAround))
                    {
                        spawnPosition = hitAround.Position;
                        surfaceNormal = hitAround.SurfaceNormal;
                        positionFound = true;
                    }

                    break;
            }

            if (!positionFound)
                return;

            // Instantiate the enemy from the prefab
            int sortKey = SortKeyBase + index;
            Entity entity = ECB.Instantiate(sortKey, Prefab);

            // Entity orientation
            float3 randomTangent = rand.NextFloat3Direction();
            float3 tangentDirection =
                math.normalize(randomTangent - math.dot(randomTangent, surfaceNormal) * surfaceNormal);

            float3 spawnOffset = rand.NextFloat(0f, 8f); // Avoid overlap
            float3 finalPosition =
                spawnPosition + (tangentDirection * spawnOffset) + (surfaceNormal * 0.5f);

            // Set Transform
            float spawnScale = Scale > 0f ? Scale : 1f;
            ECB.SetComponent(sortKey, entity, new LocalTransform
            {
                Position = finalPosition,
                Scale = spawnScale,
                Rotation = quaternion.LookRotationSafe(tangentDirection, surfaceNormal)
            });

            // Set Movement Target
            // ECB.SetComponent(sortKey, entity, new FollowTargetMovement
            ECB.SetComponent(sortKey, entity, new FlowFieldFollowerMovement());

            // Set Wave Index
            // todo @hyverno passing Enemy in lookup
            ECB.SetComponent(sortKey, entity, new Enemy { WaveIndex = WaveIndex });
        }
    }
}