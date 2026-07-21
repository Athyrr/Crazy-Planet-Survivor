using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;
using Unity.Scenes;
using UnityEngine;

/// <summary>
/// Owns the whole planet-scene streaming lifecycle: consumes <see cref="LoadSceneRequest"/>,
/// unloads the current scene, streams the new one, waits until its data is ready, then flips the
/// <see cref="GameState"/> singleton to the requested target state. Replaces the old coroutine in
/// GameManager so the sequence is deterministic, single-owner, and leak-free.
/// </summary>
[UpdateInGroup(typeof(InitializationSystemGroup))]
public partial struct SceneLoadingSystem : ISystem
{
    private EntityQuery _requestQuery;

    public void OnCreate(ref SystemState state)
    {
        _requestQuery = state.GetEntityQuery(ComponentType.ReadOnly<LoadSceneRequest>());

        state.EntityManager.CreateSingleton(
            new SceneLoadingState
            {
                Phase = ELoadPhase.Idle,
                CurrentSceneEntity = Entity.Null,
                LoadingSceneEntity = Entity.Null,
            }
        );
    }

    public void OnUpdate(ref SystemState state)
    {
        // Work on a value copy: the structural changes below (destroy/create/scene streaming) would
        // invalidate any ref held into chunk memory, so we read once and write back at the end.
        var singleton = SystemAPI.GetSingletonEntity<SceneLoadingState>();
        var loading = state.EntityManager.GetComponentData<SceneLoadingState>(singleton);

        if (loading.Phase == ELoadPhase.Idle)
            TryStartLoad(ref state, ref loading);
        else
            AdvanceLoad(ref state, ref loading);

        state.EntityManager.SetComponentData(singleton, loading);
    }

    private void TryStartLoad(ref SystemState state, ref SceneLoadingState loading)
    {
        if (_requestQuery.IsEmpty)
            return;

        // Take the first request and drop the rest: a load is exclusive, so extra requests that
        // piled up in the same window are simply rejected (concurrency guard).
        var entities = _requestQuery.ToEntityArray(Allocator.Temp);
        var request = state.EntityManager.GetComponentData<LoadSceneRequest>(entities[0]);
        entities.Dispose();
        state.EntityManager.DestroyEntity(_requestQuery);

        if (!TryResolveScene(ref state, request.PlanetID, out var sceneRef))
        {
            Debug.LogError($"[SceneLoading] Scene not found: {request.PlanetID}");
            return;
        }

        // Unload the scene currently on screen before streaming the next one in.
        if (loading.CurrentSceneEntity != Entity.Null)
        {
            SceneSystem.UnloadScene(
                state.WorldUnmanaged,
                loading.CurrentSceneEntity,
                SceneSystem.UnloadParameters.DestroyMetaEntities
            );
            loading.CurrentSceneEntity = Entity.Null;
        }

        SetGameState(ref state, EGameState.Loading);

        loading.LoadingSceneEntity = SceneSystem.LoadSceneAsync(state.WorldUnmanaged, sceneRef);
        loading.TargetState = request.TargetState;
        loading.SendStartRequest = request.SendStartRequest;
        loading.MinScreenTime = request.MinScreenTime < 0f ? 0f : request.MinScreenTime;
        loading.Timeout = request.Timeout <= 0f ? 30f : request.Timeout;
        loading.RunStartDelay = request.RunStartDelay < 0f ? 0f : request.RunStartDelay;
        loading.Timer = 0f;
        loading.Progress = 0f;
        loading.Phase = ELoadPhase.Loading;
    }

    private void AdvanceLoad(ref SystemState state, ref SceneLoadingState loading)
    {
        loading.Timer += SystemAPI.Time.DeltaTime;

        var streaming = SceneSystem.GetSceneStreamingState(
            state.WorldUnmanaged,
            loading.LoadingSceneEntity
        );

        // Bail out on a streaming failure instead of spinning on the loading screen forever.
        if (
            streaming == SceneSystem.SceneStreamingState.FailedLoadingSceneHeader
            || streaming == SceneSystem.SceneStreamingState.LoadedWithSectionErrors
        )
        {
            Debug.LogError($"[SceneLoading] Scene load failed: {streaming}");
            Abort(ref state, ref loading);
            return;
        }

        // Hard timeout guard against a stream stuck in a non-terminal state.
        if (loading.Timer > loading.Timeout)
        {
            Debug.LogError($"[SceneLoading] Scene load timed out after {loading.Timeout}s");
            Abort(ref state, ref loading);
            return;
        }

        bool streamingDone = streaming == SceneSystem.SceneStreamingState.LoadedSuccessfully;

        // Every scene bakes a PlanetData singleton; wait for it uniformly (lobby included) so we
        // never transition into a half-initialized world.
        bool dataReady = streamingDone && SystemAPI.HasSingleton<PlanetData>();

        // Cosmetic floor on the loading screen. Non-additive: a load slower than MinScreenTime
        // incurs no extra wait.
        bool minTimeElapsed = loading.Timer >= loading.MinScreenTime;

        // Update the loading-bar value (byte-weighted streaming blended with a time floor). Kept
        // monotonic so the bar never visually rewinds; forced to 1 only at the commit below.
        UpdateProgress(ref state, ref loading, dataReady);

        if (!dataReady || !minTimeElapsed)
            return;

        // Commit: the freshly streamed scene becomes the current one.
        loading.Progress = 1f;
        loading.CurrentSceneEntity = loading.LoadingSceneEntity;
        loading.LoadingSceneEntity = Entity.Null;
        loading.Phase = ELoadPhase.Idle;

        if (!loading.SendStartRequest)
        {
            // Plain load (e.g. lobby): enter the target state directly.
            SetGameState(ref state, loading.TargetState);
            return;
        }

        // Reset the run and (re)spawn the player fresh so it can play its arrival animation.
        var requestEntity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponent<StartRunRequest>(requestEntity);

        if (loading.RunStartDelay > 0f)
        {
            // Intro window: show the planet with the player but hold enemies/timer until the delay
            // elapses. RunStartSystem flips the state to the target once the countdown ends.
            SetGameState(ref state, EGameState.RunStarting);

            var countdownEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(
                countdownEntity,
                new RunStartCountdown
                {
                    Remaining = loading.RunStartDelay,
                    TargetState = loading.TargetState,
                }
            );
        }
        else
        {
            // No intro configured: start the run immediately.
            SetGameState(ref state, loading.TargetState);
        }
    }

    private void Abort(ref SystemState state, ref SceneLoadingState loading)
    {
        // Unload the half-streamed scene and drop the player back to a safe state rather than
        // trapping them on the loading screen.
        if (loading.LoadingSceneEntity != Entity.Null)
        {
            SceneSystem.UnloadScene(
                state.WorldUnmanaged,
                loading.LoadingSceneEntity,
                SceneSystem.UnloadParameters.DestroyMetaEntities
            );
            loading.LoadingSceneEntity = Entity.Null;
        }

        loading.Phase = ELoadPhase.Idle;
        SetGameState(ref state, EGameState.MainMenu);
    }

    private bool TryResolveScene(
        ref SystemState state,
        EPlanetID planetID,
        out EntitySceneReference sceneRef
    )
    {
        sceneRef = default;

        if (!SystemAPI.TryGetSingletonBuffer<PlanetSceneRefBufferElement>(out var buffer, true))
            return false;

        foreach (var scene in buffer)
        {
            if (scene.PlanetID == planetID)
            {
                sceneRef = scene.SceneReference;
                return true;
            }
        }

        return false;
    }

    private void SetGameState(ref SystemState state, EGameState newState)
    {
        if (SystemAPI.TryGetSingletonEntity<GameState>(out var entity))
            state.EntityManager.SetComponentData(entity, new GameState { State = newState });
    }

    /// <summary>
    /// Computes the loading-bar value: real byte-weighted section streaming (0 → 0.9), bumped to
    /// 0.95 once the scene data is ready, blended with a time floor so the bar always advances even
    /// for near-instant loads. Capped at 0.99 until the commit sets it to 1, and kept monotonic.
    /// </summary>
    private void UpdateProgress(ref SystemState state, ref SceneLoadingState loading, bool dataReady)
    {
        float streamProgress = ComputeStreamProgress(ref state, loading.LoadingSceneEntity);

        float realLoad = streamProgress * 0.9f;
        if (dataReady)
            realLoad = math.max(realLoad, 0.95f);

        float timeFloor = loading.MinScreenTime > 0f
            ? math.saturate(loading.Timer / loading.MinScreenTime) * 0.9f
            : 0.9f;

        float target = math.min(0.99f, math.max(realLoad, timeFloor));
        loading.Progress = math.max(loading.Progress, target);
    }

    /// <summary>
    /// Byte-weighted streaming ratio across the scene's sections using <see cref="SceneSectionData.FileSize"/>.
    /// A section counts as fully loaded when its state is Loaded, and half while Loading. Returns 0
    /// until the scene header has resolved its section list.
    /// </summary>
    private float ComputeStreamProgress(ref SystemState state, Entity sceneEntity)
    {
        var em = state.EntityManager;

        if (sceneEntity == Entity.Null || !em.HasBuffer<ResolvedSectionEntity>(sceneEntity))
            return 0f;

        var sections = em.GetBuffer<ResolvedSectionEntity>(sceneEntity);

        double total = 0.0;
        double loaded = 0.0;

        for (int i = 0; i < sections.Length; i++)
        {
            var sectionEntity = sections[i].SectionEntity;
            if (!em.HasComponent<SceneSectionData>(sectionEntity))
                continue;

            int size = em.GetComponentData<SceneSectionData>(sectionEntity).FileSize;
            if (size <= 0)
                size = 1; // guard against zero-weight sections (e.g. empty section 0)

            total += size;

            var sectionState = SceneSystem.GetSectionStreamingState(state.WorldUnmanaged, sectionEntity);
            if (sectionState == SceneSystem.SectionStreamingState.Loaded)
                loaded += size;
            else if (sectionState == SceneSystem.SectionStreamingState.Loading)
                loaded += size * 0.5;
        }

        return total <= 0.0 ? 0f : (float)(loaded / total);
    }
}
