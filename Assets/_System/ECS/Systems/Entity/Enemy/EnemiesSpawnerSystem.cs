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

    // Base (prefab) stats read at spawn to apply difficulty scaling without touching live enemies.
    private ComponentLookup<Health> _baseHealthLookup;
    private ComponentLookup<CoreStats> _baseCoreStatsLookup;
    private ComponentLookup<DamageOnContact> _baseDamageOnContactLookup;

    // Current spawn-time multipliers from time + kills (1 = no scaling). Recomputed each frame.
    private float _healthMult;
    private float _damageMult;

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

        _baseHealthLookup = state.GetComponentLookup<Health>(true);
        _baseCoreStatsLookup = state.GetComponentLookup<CoreStats>(true);
        _baseDamageOnContactLookup = state.GetComponentLookup<DamageOnContact>(true);

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

        // Difficulty scaling (time + kills): recompute the spawn-time multipliers this frame.
        _baseHealthLookup.Update(ref state);
        _baseCoreStatsLookup.Update(ref state);
        _baseDamageOnContactLookup.Update(ref state);
        EnemyScalingConfig scaleCfg = SystemAPI.TryGetSingleton<EnemyScalingConfig>(out var cfg)
            ? cfg
            : EnemyScalingConfig.Default;
        _healthMult = 1f;
        _damageMult = 1f;
        float runTimer = 0f;
        if (SystemAPI.HasSingleton<RunProgression>())
        {
            // Read RunProgression via EntityManager (not a ComponentTypeHandle/SystemAPI query read) so the
            // RunProgressionSystem write-job is completed first — avoids a read-after-write job hazard.
            var runProgEntity = SystemAPI.GetSingletonEntity<RunProgression>();
            var runProg = state.EntityManager.GetComponentData<RunProgression>(runProgEntity);
            _healthMult = scaleCfg.ComputeHealthMult(runProg.Timer, runProg.EnemiesKilledCount);
            _damageMult = scaleCfg.ComputeDamageMult(runProg.Timer, runProg.EnemiesKilledCount);
            runTimer = runProg.Timer;
        }

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);

        // Handle kills (attributed per wave)
        ProcessKills(ref ecb, ref state, ref spawnerState, waveRuntimes);
        // Handle timers, wave progression and loop restarts
        ManageWaveProgression(ref state, ref spawnerState, waves, groups, waveRuntimes, groupRuntimes);
        // Handle Spawning across every active wave
        ManageSpawning(ref ecb, ref state, ref spawnerState, waves, groups, waveRuntimes, groupRuntimes,
            settings.MaxEnemies, runTimer);
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
        DynamicBuffer<SpawnGroupRuntime> groupRuntimes, int maxEnemies, float runTimer)
    {
        int lead = spawnerState.CurrentWaveIndex;
        if (lead < 0)
            return;

        float dt = SystemAPI.Time.DeltaTime;

        // Assault director: tick its timer and ARM a pulse when it fires. Done before the budget early-out so
        // pulses stay on schedule even while at the enemy cap (an armed pulse just drains once room frees up).
        // Seed for the pulse's random bearings is derived here (SystemState is in scope for SystemAPI.Time).
        uint directorSeed = (uint)(SystemAPI.Time.ElapsedTime * 1000.0) + 13u;
        TickAssaultDirector(waves, groups, runTimer, dt, directorSeed);

        // Global per-frame safety budget shared by every active wave (frame-rate spike guard + hard cap).
        int frameBudget = math.min(MAX_SPAWNS_PER_FRAME, maxEnemies - spawnerState.ActiveEnemyCount);
        if (frameBudget <= 0)
            return;

        // The player may be mid-respawn this frame; bail before mutating any runtime so we never decrement
        // a group's Remaining without actually spawning.
        if (!SystemAPI.TryGetSingletonEntity<Player>(out var playerEntity))
            return;

        // Boss position for AroundBoss-mode groups. The boss is found by its FinalBossTag (no manual
        // capture needed); if it hasn't spawned yet, AroundBoss groups are skipped until it exists.
        bool hasBoss = SystemAPI.TryGetSingletonEntity<FinalBossTag>(out var bossEntity);
        float3 bossPosition = hasBoss
            ? SystemAPI.GetComponentRO<LocalTransform>(bossEntity).ValueRO.Position
            : float3.zero;

        // Gather every enemy to spawn this frame into ONE list, then run a SINGLE job over it. An
        // EntityCommandBuffer.ParallelWriter must be written by exactly one job, so we cannot schedule a
        // separate job per group/wave (that races the ECB's per-thread command buffers).
        // frameBudget is the exact upper bound on commands this frame, so the list never reallocates.
        var commands = new NativeList<SpawnCommand>(frameBudget, Allocator.TempJob);

        // Assault-director pulse first (highest priority): the encirclement ring must land crisp and never be
        // starved by the ambient waves. Drains the current pulse's remaining ring + horizon into this frame.
        GatherPulseSpawns(ref spawnerState, ref frameBudget, ref commands);

        // Lead wave first so the current wave is never starved by background loops...
        if (frameBudget > 0 && waveRuntimes[lead].Active)
            GatherWaveSpawns(ref spawnerState, groups, groupRuntimes, waves[lead], lead, dt, hasBoss, ref frameBudget,
                ref commands);

        // ...then the looping waves behind it.
        for (int wi = 0; wi < lead && frameBudget > 0; wi++)
        {
            if (waveRuntimes[wi].Active)
                GatherWaveSpawns(ref spawnerState, groups, groupRuntimes, waves[wi], wi, dt, hasBoss, ref frameBudget,
                    ref commands);
        }

        if (commands.Length > 0)
        {
            // Frame-invariant spawn inputs, fetched once and shared by the single job.
            var physicsWorld = SystemAPI.GetSingleton<PhysicsWorldSingleton>();
            var planetEntity = SystemAPI.GetSingletonEntity<PlanetData>();
            var planetData = SystemAPI.GetComponentRO<PlanetData>(planetEntity).ValueRO;
            var planetTransform = SystemAPI.GetComponentRO<LocalTransform>(planetEntity).ValueRO;
            var playerTransform = SystemAPI.GetComponentRO<LocalTransform>(playerEntity).ValueRO;

            var spawnJob = new SpawnJob
            {
                Commands = commands.AsArray(),
                ECB = ecb.AsParallelWriter(),
                CollisionWorld = physicsWorld.CollisionWorld,
                PlayerTransform = playerTransform,
                PlanetCenter = planetTransform.Position,
                PlanetRadius = planetData.Radius,
                BossPosition = bossPosition,
                BaseSeed = (uint)(SystemAPI.Time.ElapsedTime * 1000) + 1,
                HealthMult = _healthMult,
                DamageMult = _damageMult,
                BaseHealthLookup = _baseHealthLookup,
                BaseCoreStatsLookup = _baseCoreStatsLookup,
                BaseDamageOnContactLookup = _baseDamageOnContactLookup
            };

            systemState.Dependency = spawnJob.ScheduleParallel(commands.Length, 64, systemState.Dependency);
        }

        // Defer disposal until the scheduled job (if any) has consumed the list.
        commands.Dispose(systemState.Dependency);
    }

    /// <summary>
    /// Appends one <see cref="SpawnCommand"/> per enemy to spawn from this wave's group range, consuming the
    /// shared frame budget. The popcorn/budget contract is unchanged; only the output moved from "schedule a
    /// job per group" to "append to the frame's single spawn list".
    /// </summary>
    private void GatherWaveSpawns(ref SpawnerState spawnerState, DynamicBuffer<SpawnGroup> groups,
        DynamicBuffer<SpawnGroupRuntime> groupRuntimes, in Wave wave, int waveIndex, float dt, bool hasBoss,
        ref int frameBudget, ref NativeList<SpawnCommand> commands)
    {
        // An "around boss" wave does nothing until a final boss exists: leave every group untouched
        // (Remaining/timers preserved) so the whole wave starts the moment the boss is alive.
        if (wave.AroundBoss && !hasBoss)
            return;

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
                // GlobalIndex (enemies already released first) preserves the geometric layout across batches.
                int startIndex = group.Amount - runtime.Remaining;
                for (int k = 0; k < countToSpawn; k++)
                {
                    commands.Add(new SpawnCommand
                    {
                        Prefab = group.Prefab,
                        Mode = group.Mode,
                        AroundBoss = wave.AroundBoss,
                        GlobalIndex = startIndex + k,
                        TotalAmount = group.Amount,
                        SpawnOrigin = group.Position,
                        MinRange = group.MinRange,
                        MaxRange = group.MaxRange,
                        WaveIndex = waveIndex,
                        Scale = group.Scale
                    });
                }

                runtime.Remaining -= countToSpawn;
                spawnerState.ActiveEnemyCount += countToSpawn;
                frameBudget -= countToSpawn;
            }

            groupRuntimes[gi] = runtime;
        }
    }

    /// <summary>
    /// Ticks the assault director and ARMS a pulse when its timer fires. A pulse seeds the ring + horizon
    /// "remaining" counts (drained later this/next frames by <see cref="GatherPulseSpawns"/> through the shared
    /// budget). Pulse SIZE and FREQUENCY interpolate with run time so peaks grow bigger and closer together.
    /// No-op (and safe) if the director components aren't present (subscene baked before the director existed).
    /// </summary>
    private void TickAssaultDirector(DynamicBuffer<Wave> waves, DynamicBuffer<SpawnGroup> groups, float runTimer,
        float dt, uint randSeed)
    {
        if (!SystemAPI.TryGetSingleton<SpawnIntensityConfig>(out var cfg) || !cfg.Enabled)
            return;
        if (!SystemAPI.HasSingleton<SpawnIntensityState>())
            return;

        ref var st = ref SystemAPI.GetSingletonRW<SpawnIntensityState>().ValueRW;

        st.PulseTimer -= dt;
        if (st.PulseTimer > 0f)
            return;

        // A pulse is still draining: hold (timer stays <= 0) so we never stack two pulses at once. It re-arms
        // the moment the current burst has fully spawned.
        if (st.RingRemaining > 0 || st.HorizonRemaining > 0 || st.DirRemaining > 0)
            return;

        // Resolve the pulse prefab: explicit config prefab, else the first authored group's prefab.
        Entity prefab = cfg.AssaultPrefab != Entity.Null ? cfg.AssaultPrefab : ResolveFallbackPulsePrefab(waves, groups);

        float t = cfg.RampT(runTimer);
        float period = math.lerp(cfg.PulsePeriodStart, cfg.PulsePeriodMin, t);
        st.PulseTimer = math.max(MIN_LOOP_PERIOD, period);

        if (prefab == Entity.Null)
            return; // nothing to spawn this cycle; timer already reloaded so we retry next period

        int ring = (int)math.round(math.lerp(cfg.RingCountStart, cfg.RingCountMax, t));
        int horizon = (int)math.round(math.lerp(cfg.HorizonCountStart, cfg.HorizonCountMax, t));
        int dir = (int)math.round(math.lerp(cfg.DirCountStart, cfg.DirCountMax, t));

        st.PulsePrefab = prefab;
        st.RingTotal = math.max(0, ring);
        st.RingRemaining = st.RingTotal;
        st.HorizonTotal = math.max(0, horizon);
        st.HorizonRemaining = st.HorizonTotal;

        // Directional walls: pick 1..DirMaxClusters random bearings so each pulse's masses come from fresh
        // angles. When two walls, keep them apart (~80°..280°) so they read as two distinct assaults.
        st.DirTotal = math.max(0, dir);
        st.DirRemaining = st.DirTotal;
        var rng = Random.CreateFromIndex(randSeed);
        const float twoPi = 6.28318530718f;
        int maxClusters = math.clamp(cfg.DirMaxClusters, 1, 2); // only two bearings are stored
        st.DirClusterCount = maxClusters <= 1 ? 1 : rng.NextInt(1, maxClusters + 1);
        st.DirCenter0 = rng.NextFloat(0f, twoPi);
        st.DirCenter1 = st.DirCenter0 + rng.NextFloat(1.4f, twoPi - 1.4f);
    }

    /// <summary> First valid prefab in wave 0's groups, used when no explicit assault prefab is configured. </summary>
    private static Entity ResolveFallbackPulsePrefab(DynamicBuffer<Wave> waves, DynamicBuffer<SpawnGroup> groups)
    {
        if (waves.Length == 0)
            return Entity.Null;

        Wave w0 = waves[0];
        int end = w0.GroupStartIndex + w0.GroupCount;
        for (int gi = w0.GroupStartIndex; gi < end && gi < groups.Length; gi++)
        {
            if (groups[gi].Prefab != Entity.Null)
                return groups[gi].Prefab;
        }

        return Entity.Null;
    }

    /// <summary>
    /// Appends the current assault pulse's spawns (near encirclement ring, then far horizon mass) to this
    /// frame's command list, consuming the shared budget. Both reuse existing spawn modes so the whole spawn
    /// job (surface snap, difficulty scaling, ECB) applies unchanged. Pulse enemies carry WaveIndex = -1 so
    /// their kills never credit a wave's kill-% (they don't belong to any authored wave).
    /// </summary>
    private void GatherPulseSpawns(ref SpawnerState spawnerState, ref int frameBudget,
        ref NativeList<SpawnCommand> commands)
    {
        if (frameBudget <= 0)
            return;
        if (!SystemAPI.TryGetSingleton<SpawnIntensityConfig>(out var cfg) || !cfg.Enabled)
            return;
        if (!SystemAPI.HasSingleton<SpawnIntensityState>())
            return;

        ref var st = ref SystemAPI.GetSingletonRW<SpawnIntensityState>().ValueRW;
        if (st.PulsePrefab == Entity.Null)
            return;

        // Near encirclement ring (the "cornered" moment): CircleAroundPlayer, tight radius, spawned ON the
        // player so a max-speed player can't pre-empt it.
        int ringToSpawn = math.min(frameBudget, st.RingRemaining);
        for (int k = 0; k < ringToSpawn; k++)
        {
            commands.Add(new SpawnCommand
            {
                Prefab = st.PulsePrefab,
                Mode = SpawnMode.CircleAroundPlayer,
                AroundBoss = false,
                GlobalIndex = (st.RingTotal - st.RingRemaining) + k,
                TotalAmount = st.RingTotal,
                SpawnOrigin = float3.zero,
                MinRange = 0f,
                MaxRange = cfg.RingRadius,
                WaveIndex = -1,
                Scale = 0f
            });
        }
        if (ringToSpawn > 0)
        {
            st.RingRemaining -= ringToSpawn;
            spawnerState.ActiveEnemyCount += ringToSpawn;
            frameBudget -= ringToSpawn;
        }

        // Far mass over the horizon (the "invasion"): AroundPlayer band, appears at distance and streams in.
        int horizonToSpawn = math.min(frameBudget, st.HorizonRemaining);
        for (int k = 0; k < horizonToSpawn; k++)
        {
            commands.Add(new SpawnCommand
            {
                Prefab = st.PulsePrefab,
                Mode = SpawnMode.AroundPlayer,
                AroundBoss = false,
                GlobalIndex = (st.HorizonTotal - st.HorizonRemaining) + k,
                TotalAmount = st.HorizonTotal,
                SpawnOrigin = float3.zero,
                MinRange = cfg.HorizonMinRange,
                MaxRange = cfg.HorizonMaxRange,
                WaveIndex = -1,
                Scale = 0f
            });
        }
        if (horizonToSpawn > 0)
        {
            st.HorizonRemaining -= horizonToSpawn;
            spawnerState.ActiveEnemyCount += horizonToSpawn;
            frameBudget -= horizonToSpawn;
        }

        // Directional walls (the multi-angle assault): each enemy is assigned round-robin to one of the
        // pulse's clusters and carries that cluster's bearing; the job scatters it inside the sector.
        int dirToSpawn = math.min(frameBudget, st.DirRemaining);
        int clusterCount = math.max(1, st.DirClusterCount);
        for (int k = 0; k < dirToSpawn; k++)
        {
            int gi = (st.DirTotal - st.DirRemaining) + k;
            float center = (gi % clusterCount) == 0 ? st.DirCenter0 : st.DirCenter1;
            commands.Add(new SpawnCommand
            {
                Prefab = st.PulsePrefab,
                Mode = SpawnMode.SectorAroundPlayer,
                AroundBoss = false,
                GlobalIndex = gi,
                TotalAmount = st.DirTotal,
                SpawnOrigin = float3.zero,
                MinRange = cfg.DirMinRange,
                MaxRange = cfg.DirMaxRange,
                WaveIndex = -1,
                Scale = 0f,
                ArcCenter = center,
                ArcWidth = cfg.DirArcWidth
            });
        }
        if (dirToSpawn > 0)
        {
            st.DirRemaining -= dirToSpawn;
            spawnerState.ActiveEnemyCount += dirToSpawn;
            frameBudget -= dirToSpawn;
        }
    }

    /// <summary> One enemy to spawn this frame: its group's placement params plus its per-entity index. </summary>
    private struct SpawnCommand
    {
        public Entity Prefab;
        public SpawnMode Mode;
        public bool AroundBoss; // wave-level: spawn around the boss instead of using Mode
        public int GlobalIndex; // index within its group, for the geometric layout
        public int TotalAmount; // group total, for the geometric layout
        public float3 SpawnOrigin;
        public float MinRange;
        public float MaxRange;
        public int WaveIndex;
        public float Scale;

        // SectorAroundPlayer only: bearing (radians) the wall is centered on, and its angular width.
        public float ArcCenter;
        public float ArcWidth;
    }

    [BurstCompile]
    private struct SpawnJob : IJobFor
    {
        [ReadOnly] public NativeArray<SpawnCommand> Commands;
        public EntityCommandBuffer.ParallelWriter ECB;

        [ReadOnly] public CollisionWorld CollisionWorld;
        [ReadOnly] public LocalTransform PlayerTransform;

        // Planet Data (shared by every command this frame)
        public float3 PlanetCenter;
        public float PlanetRadius;

        // Live boss position, shared by AroundBoss commands (valid only when such commands were gathered).
        public float3 BossPosition;

        // Frame-stable base; combined with the command index for a unique per-entity seed.
        public uint BaseSeed;

        // Difficulty scaling (1 = none). Base stats are read from the prefab entity.
        public float HealthMult;
        public float DamageMult;
        [ReadOnly] public ComponentLookup<Health> BaseHealthLookup;
        [ReadOnly] public ComponentLookup<CoreStats> BaseCoreStatsLookup;
        [ReadOnly] public ComponentLookup<DamageOnContact> BaseDamageOnContactLookup;

        public void Execute(int index)
        {
            SpawnCommand cmd = Commands[index];
            int globalIndex = cmd.GlobalIndex;
            // index is unique across every command this frame -> unique seed and unique ECB sort key.
            var rand = Random.CreateFromIndex(BaseSeed + (uint)index);

            float3 spawnPosition = float3.zero;
            float3 surfaceNormal = float3.zero;
            bool positionFound = false;

            var groundFilter = new CollisionFilter
            {
                BelongsTo = CollisionLayers.Raycast,
                CollidesWith = CollisionLayers.Landscape
            };

            // Around-boss waves spawn in a spiral centered on the boss's live position, overriding the
            // group's mode. Everything else uses the group's SpawnMode.
            if (cmd.AroundBoss)
            {
                float goldenAngleBoss = 2.39996323f;
                float cBoss = cmd.MaxRange / math.sqrt(cmd.TotalAmount);
                float spiralRadiusBoss = cBoss * math.sqrt(globalIndex) + cmd.MinRange;
                float angleBoss = globalIndex * goldenAngleBoss;
                float2 circleBoss = new float2(math.cos(angleBoss), math.sin(angleBoss)) * spiralRadiusBoss;

                float3 upBoss = math.normalize(BossPosition - PlanetCenter);
                float3 tangentBoss = math.cross(upBoss, new float3(0, 1, 0));
                if (math.lengthsq(tangentBoss) < 0.001f)
                    tangentBoss = math.cross(upBoss, new float3(1, 0, 0));

                quaternion alignBoss = quaternion.LookRotationSafe(tangentBoss, upBoss);
                float3 worldOffBoss = math.rotate(alignBoss, new float3(circleBoss.x, 0f, circleBoss.y));
                float3 pBoss = BossPosition + worldOffBoss;

                if (PlanetUtils.SnapToSurfaceRaycast(ref CollisionWorld, pBoss, PlanetCenter, groundFilter, 100f,
                        out var hitBoss))
                {
                    spawnPosition = hitBoss.Position;
                    surfaceNormal = hitBoss.SurfaceNormal;
                    positionFound = true;
                }
            }
            // Calculate spawn position based on spawning mode
            else switch (cmd.Mode)
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

                    float maxAmount = math.max(1f, (float)cmd.TotalAmount);
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

                    float c = cmd.MaxRange / math.sqrt(cmd.TotalAmount);

                    float angle = globalIndex * goldenAngle;
                    float spiralRadius = c * math.sqrt(globalIndex);

                    spiralRadius += cmd.MinRange;

                    float2 perfectCircle = new float2(math.cos(angle), math.sin(angle)) * spiralRadius;

                    float3 up = math.normalize(cmd.SpawnOrigin - PlanetCenter);
                    float3 tangent = math.cross(up, new float3(0, 1, 0));
                    if (math.lengthsq(tangent) < 0.001f)
                        tangent = math.cross(up, new float3(1, 0, 0));

                    quaternion alignmentRot = quaternion.LookRotationSafe(tangent, up);
                    float3 localOffset = new float3(perfectCircle.x, 0f, perfectCircle.y);
                    float3 worldOffset = math.rotate(alignmentRot, localOffset);
                    float3 p = cmd.SpawnOrigin + worldOffset;

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
                    float oppositePositionRadius = math.max(15f, cmd.TotalAmount * 0.5f);

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

                    float fraction = (float)globalIndex / cmd.TotalAmount;

                    float radiusAround = math.lerp(cmd.MinRange, cmd.MaxRange, math.sqrt(fraction));

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

                case SpawnMode.CircleAroundPlayer:
                    // Uniform ring: enemy i sits at angle i*(2*PI/N) on a single circle of radius MaxRange,
                    // centered on the player's live position. Equal angular spacing => a perfect circle once
                    // the whole group is out (set the group's SpawnDelay to 0 for an instant ring; a positive
                    // delay releases enemies one-by-one, sweeping around the circle).
                    const float twoPiCircle = 6.28318530718f;
                    float countCircle = math.max(1f, (float)cmd.TotalAmount);
                    float angleCircle = globalIndex * (twoPiCircle / countCircle);
                    float radiusCircle = cmd.MaxRange;

                    float2 ringPoint = new float2(math.cos(angleCircle), math.sin(angleCircle)) * radiusCircle;

                    // Build the player's local tangent frame on the sphere, same as AroundPlayer, so the ring
                    // lies flat on the surface around the player wherever they stand.
                    float3 centerCircle = PlayerTransform.Position;
                    float3 upCircle = math.normalize(centerCircle - PlanetCenter);
                    float3 tangentCircle = math.cross(upCircle, new float3(0, 1, 0));
                    if (math.lengthsq(tangentCircle) < 0.001f)
                        tangentCircle = math.cross(upCircle, new float3(1, 0, 0));

                    quaternion alignmentRotCircle = quaternion.LookRotationSafe(tangentCircle, upCircle);
                    float3 localOffsetCircle = new float3(ringPoint.x, 0f, ringPoint.y);
                    float3 worldOffsetCircle = math.rotate(alignmentRotCircle, localOffsetCircle);
                    float3 roughPosCircle = centerCircle + worldOffsetCircle;

                    if (PlanetUtils.SnapToSurfaceRaycast(ref CollisionWorld, roughPosCircle, PlanetCenter,
                            groundFilter, 100f, out var hitCircle))
                    {
                        spawnPosition = hitCircle.Position;
                        surfaceNormal = hitCircle.SurfaceNormal;
                        positionFound = true;
                    }

                    break;

                case SpawnMode.SectorAroundPlayer:
                    // Directional wall: scatter the enemy inside an angular SECTOR centered on cmd.ArcCenter
                    // (width cmd.ArcWidth), at a random distance in [MinRange, MaxRange], around the player's
                    // live position. Organic fill (per-enemy random) so the wall reads as a converging mass,
                    // not a rigid arc. Several walls at different ArcCenter => a multi-angle assault.
                    float halfWidthSector = cmd.ArcWidth * 0.5f;
                    float bearingSector = cmd.ArcCenter + rand.NextFloat(-halfWidthSector, halfWidthSector);
                    float distSector = rand.NextFloat(cmd.MinRange, cmd.MaxRange);
                    float2 offsetSector =
                        new float2(math.cos(bearingSector), math.sin(bearingSector)) * distSector;

                    float3 centerSector = PlayerTransform.Position;
                    float3 upSector = math.normalize(centerSector - PlanetCenter);
                    float3 tangentSector = math.cross(upSector, new float3(0, 1, 0));
                    if (math.lengthsq(tangentSector) < 0.001f)
                        tangentSector = math.cross(upSector, new float3(1, 0, 0));

                    quaternion alignmentRotSector = quaternion.LookRotationSafe(tangentSector, upSector);
                    float3 localOffsetSector = new float3(offsetSector.x, 0f, offsetSector.y);
                    float3 worldOffsetSector = math.rotate(alignmentRotSector, localOffsetSector);
                    float3 roughPosSector = centerSector + worldOffsetSector;

                    if (PlanetUtils.SnapToSurfaceRaycast(ref CollisionWorld, roughPosSector, PlanetCenter,
                            groundFilter, 120f, out var hitSector))
                    {
                        spawnPosition = hitSector.Position;
                        surfaceNormal = hitSector.SurfaceNormal;
                        positionFound = true;
                    }

                    break;
            }

            if (!positionFound)
                return;

            // Instantiate the enemy from the prefab
            Entity entity = ECB.Instantiate(index, cmd.Prefab);

            // Difficulty HP scaling (time + kills): override the instantiated copy's Health from the
            // prefab's base value, so a unit's toughness is fixed by WHEN it spawned (not re-buffed later).
            if (HealthMult > 1f)
            {
                if (BaseHealthLookup.HasComponent(cmd.Prefab))
                {
                    int baseHp = BaseHealthLookup[cmd.Prefab].Value;
                    ECB.SetComponent(index, entity, new Health { Value = (int)math.round(baseHp * HealthMult) });
                }

                if (BaseCoreStatsLookup.HasComponent(cmd.Prefab))
                {
                    var cs = BaseCoreStatsLookup[cmd.Prefab];
                    cs.MaxHealth *= HealthMult;
                    ECB.SetComponent(index, entity, cs);
                }
            }

            // Difficulty contact-damage scaling: a unit's hit is fixed by WHEN it spawned, like its HP.
            if (DamageMult > 1f && BaseDamageOnContactLookup.HasComponent(cmd.Prefab))
            {
                var doc = BaseDamageOnContactLookup[cmd.Prefab];
                doc.Damage *= DamageMult;
                ECB.SetComponent(index, entity, doc);
            }

            // Entity orientation
            float3 randomTangent = rand.NextFloat3Direction();
            float3 tangentDirection =
                math.normalize(randomTangent - math.dot(randomTangent, surfaceNormal) * surfaceNormal);

            // Anti-overlap jitter, skipped for the clean uniform ring so the circle stays crisp (the random
            // orientation above is kept; only the positional offset is dropped).
            bool cleanRing = !cmd.AroundBoss && cmd.Mode == SpawnMode.CircleAroundPlayer;
            float spawnOffset = cleanRing ? 0f : rand.NextFloat(0f, 8f); // Avoid overlap
            float3 finalPosition =
                spawnPosition + (tangentDirection * spawnOffset) + (surfaceNormal * 0.5f);

            // Set Transform
            float spawnScale = cmd.Scale > 0f ? cmd.Scale : 1f;
            ECB.SetComponent(index, entity, new LocalTransform
            {
                Position = finalPosition,
                Scale = spawnScale,
                Rotation = quaternion.LookRotationSafe(tangentDirection, surfaceNormal)
            });

            // Do NOT re-set FlowFieldFollowerMovement here: it carries the prefab's baked Acceleration /
            // MaxTurnRateDeg, which a default-valued write would wipe on every spawn.

            // Set Wave Index
            // todo @hyverno passing Enemy in lookup
            ECB.SetComponent(index, entity, new Enemy { WaveIndex = cmd.WaveIndex });
        }
    }
}