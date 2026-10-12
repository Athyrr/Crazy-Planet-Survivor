using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Plays a launch-pad flight started by <see cref="LaunchPadSystem"/>: wind-up on the pad, take-off
/// into the sky, streaming of the target planet through the regular <see cref="SceneLoadingSystem"/>
/// (keeping the player and the run alive), then the fall onto the arrival point and the touchdown.
/// Gameplay stays frozen in <see cref="EGameState.PlanetTransfer"/> until the player touches down.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[UpdateAfter(typeof(LaunchPadSystem))]
public partial struct PlanetTransferSystem : ISystem
{
    private const float TOUCHDOWN_DURATION = 0.3f;
    private const float LOADING_MIN_SCREEN_TIME = 0.6f;
    private const float LOADING_TIMEOUT = 30f;
    private const float MIN_PLAYER_SCALE = 0.02f;

    private EntityQuery _runScopeQuery;
    private EntityQuery _padsQuery;

    public void OnCreate(ref SystemState state)
    {
        _runScopeQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<RunScope>()
            .WithNone<RunProgression>()
            .Build(ref state);

        _padsQuery = state.GetEntityQuery(ComponentType.ReadOnly<LaunchPad>(), ComponentType.ReadOnly<LocalTransform>());

        state.RequireForUpdate<PlanetTransfer>();
        state.RequireForUpdate<GameState>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var transferEntity = SystemAPI.GetSingletonEntity<PlanetTransfer>();
        var transfer = SystemAPI.GetComponent<PlanetTransfer>(transferEntity);
        var gameState = SystemAPI.GetSingleton<GameState>().State;

        // The run was left (back to lobby / main menu, or the scene load aborted): drop the flight.
        if (gameState == EGameState.MainMenu || gameState == EGameState.Lobby || gameState == EGameState.GameOver
            || !SystemAPI.TryGetSingletonEntity<Player>(out var playerEntity))
        {
            Finish(ref state, transferEntity, Entity.Null, EGameState.PlanetTransfer);
            return;
        }

        // Scene streaming owns the GameState while it runs; only the Loading phase may wait through it.
        if (transfer.Phase != EPlanetTransferPhase.Loading && gameState != EGameState.PlanetTransfer)
            return;

        var player = SystemAPI.GetComponent<LocalTransform>(playerEntity);
        float dt = SystemAPI.Time.DeltaTime;
        transfer.Timer += dt;

        switch (transfer.Phase)
        {
            case EPlanetTransferPhase.Charge:
                UpdateCharge(ref transfer, ref player, dt);
                break;
            case EPlanetTransferPhase.Launch:
                UpdateLaunch(ref state, ref transfer, ref player, dt);
                break;
            case EPlanetTransferPhase.Loading:
                if (!UpdateLoading(ref state, ref transfer, ref player, gameState))
                {
                    SystemAPI.SetComponent(transferEntity, transfer);
                    return;
                }
                break;
            case EPlanetTransferPhase.Landing:
                UpdateLanding(ref transfer, ref player, dt);
                break;
            case EPlanetTransferPhase.Touchdown:
                if (UpdateTouchdown(ref transfer, ref player))
                {
                    SystemAPI.SetComponent(playerEntity, player);
                    Finish(ref state, transferEntity, playerEntity, EGameState.Running);
                    return;
                }
                break;
        }

        SystemAPI.SetComponent(playerEntity, player);
        SystemAPI.SetComponent(transferEntity, transfer);
    }

    // ── Phases ──────────────────────────────────────────────────────────────

    /// <summary>Pulls the player onto the pad center while the spin winds up and the player crouches.</summary>
    private static void UpdateCharge(ref PlanetTransfer t, ref LocalTransform player, float dt)
    {
        float k = t.ChargeDuration > 0f ? math.saturate(t.Timer / t.ChargeDuration) : 1f;

        t.SpinAngle += t.SpinSpeed * k * k * dt;

        player.Position = math.lerp(t.StartPosition, t.Anchor, LaunchPadMath.EaseOutCubic(k));
        player.Rotation = SpunRotation(t);
        player.Scale = 1f - 0.18f * math.sin(k * math.PI * 0.5f);

        if (k >= 1f)
            NextPhase(ref t, EPlanetTransferPhase.Launch);
    }

    /// <summary>Shoots the player up along the pad normal, accelerating, then shrinks it to a speck.</summary>
    private void UpdateLaunch(ref SystemState state, ref PlanetTransfer t, ref LocalTransform player, float dt)
    {
        float k = math.saturate(t.Timer / t.LaunchDuration);

        t.SpinAngle += t.SpinSpeed * dt;

        // Strong kick off the pad then keep accelerating away from the planet.
        float height = t.FlightHeight * (0.25f * k + 0.75f * k * k);
        float pop = 0.82f + 0.38f * LaunchPadMath.EaseOutBack(math.saturate(k / 0.2f));
        float vanish = 1f - math.saturate((k - 0.65f) / 0.35f);

        player.Position = t.Anchor + t.AnchorNormal * height;
        player.Rotation = SpunRotation(t);
        player.Scale = math.max(MIN_PLAYER_SCALE, pop * vanish);

        if (k < 1f)
            return;

        var planet = SystemAPI.GetSingleton<PlanetData>();
        if (planet.PlanetID == t.TargetPlanet)
        {
            // Hop to another pad of the same planet: no scene swap, the run keeps its enemies.
            NextPhase(ref t, EPlanetTransferPhase.Loading);
            return;
        }

        if (!IsPlanetRegistered(ref state, t.TargetPlanet))
        {
            Debug.LogError($"[LaunchPad] {t.TargetPlanet} has no scene in GameScenesConfig, landing back on the pad.");
            NextPhase(ref t, EPlanetTransferPhase.Landing);
            return;
        }

        // The old planet's enemies, orbs and loot are left behind; the player, its stats/spells and the
        // run progression entity are runtime entities and survive the scene swap.
        // Array overload so the enemies' LinkedEntityGroup children go with them (same as RunCleanerSystem).
        var leftBehind = _runScopeQuery.ToEntityArray(Allocator.Temp);
        state.EntityManager.DestroyEntity(leftBehind);
        leftBehind.Dispose();

        var request = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(request, new LoadSceneRequest
        {
            PlanetID = t.TargetPlanet,
            TargetState = EGameState.PlanetTransfer,
            SendStartRequest = false,
            MinScreenTime = LOADING_MIN_SCREEN_TIME,
            Timeout = LOADING_TIMEOUT,
            RunStartDelay = 0f,
        });

        NextPhase(ref t, EPlanetTransferPhase.Loading);
    }

    /// <summary>
    /// Waits for the target planet to be streamed and its pads placed, then drops the player high above
    /// the arrival point. Returns false while still waiting.
    /// </summary>
    private bool UpdateLoading(ref SystemState state, ref PlanetTransfer t, ref LocalTransform player, EGameState gameState)
    {
        if (gameState != EGameState.PlanetTransfer)
            return false;

        if (!SystemAPI.TryGetSingleton<PlanetData>(out var planet) || planet.PlanetID != t.TargetPlanet)
            return false;

        if (!TryResolveArrival(ref state, t, planet, out float3 arrival, out quaternion facingHint, out bool ready))
            return false;

        if (!ready)
            return false;

        float3 normal = math.normalize(arrival - planet.Center);

        t.Anchor = arrival;
        t.AnchorNormal = normal;
        t.BaseRotation = LaunchPadMath.AlignToUp(math.forward(facingHint), normal);

        if (SystemAPI.TryGetSingletonRW<RunProgression>(out var progression) && progression.ValueRO.PlanetID != planet.PlanetID)
        {
            // New planet, new stage: its own timer and waves, but the kill count keeps adding up.
            progression.ValueRW.PlanetID = planet.PlanetID;
            progression.ValueRW.Timer = 0f;
            progression.ValueRW.ProgressRatio = 0f;
            progression.ValueRW.EnemiesKilledRatio = 0f;
        }

        player.Position = t.Anchor + t.AnchorNormal * t.FlightHeight;
        player.Rotation = SpunRotation(t);
        player.Scale = MIN_PLAYER_SCALE;

        NextPhase(ref t, EPlanetTransferPhase.Landing);
        return true;
    }

    /// <summary>Falls from the sky and brakes softly onto the arrival point, the spin winding down.</summary>
    private static void UpdateLanding(ref PlanetTransfer t, ref LocalTransform player, float dt)
    {
        float k = math.saturate(t.Timer / t.LandingDuration);

        float spin = (1f - k) * (1f - k);
        t.SpinAngle += t.SpinSpeed * spin * dt;

        float height = t.FlightHeight * (1f - LaunchPadMath.EaseOutCubic(k));
        float grow = LaunchPadMath.EaseOutBack(math.saturate(k / 0.35f));

        player.Position = t.Anchor + t.AnchorNormal * height;
        player.Rotation = SpunRotation(t);
        player.Scale = math.max(MIN_PLAYER_SCALE, grow);

        if (k >= 1f)
            NextPhase(ref t, EPlanetTransferPhase.Touchdown);
    }

    /// <summary>Small squash on impact. Returns true once control can be handed back.</summary>
    private static bool UpdateTouchdown(ref PlanetTransfer t, ref LocalTransform player)
    {
        float k = math.saturate(t.Timer / TOUCHDOWN_DURATION);

        player.Position = t.Anchor;
        player.Rotation = SpunRotation(t);
        player.Scale = 1f - 0.2f * math.sin(k * math.PI);

        if (k < 1f)
            return false;

        player.Scale = 1f;
        return true;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Finds where to land on the loaded planet: the pad matching <see cref="PlanetTransfer.TargetPadID"/>,
    /// else the planet's PlayerStart, else the planet's north pole. <paramref name="ready"/> is false while
    /// the target pad is still being dropped onto the terrain.
    /// </summary>
    private bool TryResolveArrival(ref SystemState state, in PlanetTransfer t, in PlanetData planet,
        out float3 position, out quaternion facing, out bool ready)
    {
        ready = true;

        if (t.TargetPadID != 0)
        {
            var pads = _padsQuery.ToComponentDataArray<LaunchPad>(Allocator.Temp);
            var transforms = _padsQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

            for (int i = 0; i < pads.Length; i++)
            {
                if (pads[i].PadID != t.TargetPadID)
                    continue;

                position = transforms[i].Position;
                facing = transforms[i].Rotation;
                ready = pads[i].Placed;
                return true;
            }

            Debug.LogWarning($"[LaunchPad] No pad {t.TargetPadID} on {planet.PlanetID}, landing on its PlayerStart.");
        }

        if (SystemAPI.TryGetSingleton<PlayerStart>(out var start))
        {
            position = start.Position;
            facing = start.Rotation;
            return true;
        }

        position = planet.Center + math.up() * (planet.Radius + 2f);
        facing = quaternion.identity;
        return true;
    }

    private bool IsPlanetRegistered(ref SystemState state, EPlanetID planetID)
    {
        if (!SystemAPI.TryGetSingletonBuffer<PlanetSceneRefBufferElement>(out var scenes, true))
            return false;

        foreach (var scene in scenes)
        {
            if (scene.PlanetID == planetID)
                return true;
        }

        return false;
    }

    private static quaternion SpunRotation(in PlanetTransfer t) =>
        math.mul(quaternion.AxisAngle(t.AnchorNormal, t.SpinAngle), t.BaseRotation);

    private static void NextPhase(ref PlanetTransfer t, EPlanetTransferPhase phase)
    {
        t.Phase = phase;
        t.Timer = 0f;
    }

    /// <summary>Ends the flight: restores the player, removes the transfer and hands the game back.</summary>
    private void Finish(ref SystemState state, Entity transferEntity, Entity playerEntity, EGameState nextState)
    {
        if (playerEntity != Entity.Null)
        {
            var player = SystemAPI.GetComponent<LocalTransform>(playerEntity);
            player.Scale = 1f;
            SystemAPI.SetComponent(playerEntity, player);
        }

        state.EntityManager.DestroyEntity(transferEntity);

        if (nextState != EGameState.PlanetTransfer)
        {
            var gameStateEntity = SystemAPI.GetSingletonEntity<GameState>();
            state.EntityManager.SetComponentData(gameStateEntity, new GameState { State = nextState });
        }
    }
}
