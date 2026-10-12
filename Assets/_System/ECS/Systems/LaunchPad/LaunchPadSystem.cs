using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;

/// <summary>
/// Owns the launch pads of the loaded planet: drops them onto the terrain once the planet is
/// streamed in, animates their floating star, arms them once the player has stepped away, and starts
/// a <see cref="PlanetTransfer"/> when the player walks onto an armed pad during a run.
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct LaunchPadSystem : ISystem
{
    // Pads give up looking for the ground after this many frames (planet without Landscape collider).
    private const int MAX_PLACE_ATTEMPTS = 180;

    // The player must get this much further than the trigger radius before a pad re-arms.
    private const float REARM_RADIUS_MULT = 1.25f;

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<LaunchPad>();
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<GameState>();
    }

    public void OnUpdate(ref SystemState state)
    {
        var planet = SystemAPI.GetSingleton<PlanetData>();
        var gameState = SystemAPI.GetSingleton<GameState>().State;

        PlacePads(ref state, planet);
        AnimateVisuals(ref state);

        if (!SystemAPI.TryGetSingletonEntity<Player>(out var playerEntity))
            return;

        var player = SystemAPI.GetComponent<LocalTransform>(playerEntity);
        bool transferInFlight = SystemAPI.HasSingleton<PlanetTransfer>();
        bool canLaunch = gameState == EGameState.Running && !transferInFlight;

        float runProgress = SystemAPI.TryGetSingleton<RunProgression>(out var progression)
            ? progression.ProgressRatio
            : 0f;

        // The transfer entity is created after the loop: no structural change while iterating.
        bool launch = false;
        LaunchPad firedPad = default;
        LocalTransform firedPadTransform = default;

        foreach (var (pad, transform) in SystemAPI.Query<RefRW<LaunchPad>, RefRO<LocalTransform>>())
        {
            if (!pad.ValueRO.Placed)
                continue;

            float radius = pad.ValueRO.TriggerRadius;
            float distSq = math.distancesq(player.Position, transform.ValueRO.Position);

            if (!pad.ValueRO.Armed)
            {
                float rearm = radius * REARM_RADIUS_MULT;
                if (distSq > rearm * rearm)
                    pad.ValueRW.Armed = true;
                continue;
            }

            if (!canLaunch || distSq > radius * radius)
                continue;

            if (pad.ValueRO.TargetPlanet == EPlanetID.None || runProgress < pad.ValueRO.UnlockAtRunProgress)
                continue;

            pad.ValueRW.Armed = false;
            firedPad = pad.ValueRO;
            firedPadTransform = transform.ValueRO;
            launch = true;
            canLaunch = false;
        }

        if (launch)
            BeginTransfer(ref state, firedPad, firedPadTransform, player, planet);
    }

    /// <summary>Raycasts not-yet-placed pads onto the Landscape and aligns them with the planet up.</summary>
    private void PlacePads(ref SystemState state, PlanetData planet)
    {
        if (!SystemAPI.TryGetSingleton<PhysicsWorldSingleton>(out var physics))
            return;

        var collisionWorld = physics.CollisionWorld;
        var filter = new CollisionFilter
        {
            BelongsTo = CollisionLayers.Raycast,
            CollidesWith = CollisionLayers.Landscape,
        };

        foreach (var (pad, transform) in SystemAPI.Query<RefRW<LaunchPad>, RefRW<LocalTransform>>())
        {
            if (pad.ValueRO.Placed)
                continue;

            float3 position = transform.ValueRO.Position;
            float rayHeight = math.max(planet.Radius, 50f);

            if (PlanetUtils.SnapToSurfaceRaycast(ref collisionWorld, position, planet.Center, filter, rayHeight,
                    out var hit))
            {
                float3 up = math.normalize(hit.Position - planet.Center);

                transform.ValueRW.Position = hit.Position;
                transform.ValueRW.Rotation = LaunchPadMath.AlignToUp(math.forward(transform.ValueRO.Rotation), up);
                pad.ValueRW.Placed = true;
            }
            else if (++pad.ValueRW.PlaceAttempts >= MAX_PLACE_ATTEMPTS)
            {
                Debug.LogWarning($"[LaunchPad] Pad {pad.ValueRO.PadID} found no Landscape below it, kept at its authored position.");
                pad.ValueRW.Placed = true;
            }
        }
    }

    /// <summary>Spins and bobs the decorative star of each pad (local space, so it follows the pad).</summary>
    private void AnimateVisuals(ref SystemState state)
    {
        float time = (float)SystemAPI.Time.ElapsedTime;

        foreach (var (visual, transform) in SystemAPI.Query<RefRO<LaunchPadVisual>, RefRW<LocalTransform>>())
        {
            var v = visual.ValueRO;
            float bob = math.sin(time * v.BobFrequency * 2f * math.PI) * v.BobAmplitude;

            transform.ValueRW.Position = v.BasePosition + new float3(0f, bob, 0f);
            transform.ValueRW.Rotation = math.mul(quaternion.AxisAngle(math.up(), time * v.SpinSpeed), v.BaseRotation);
        }
    }

    private void BeginTransfer(ref SystemState state, in LaunchPad pad, in LocalTransform padTransform,
        in LocalTransform player, in PlanetData planet)
    {
        float3 normal = math.normalize(padTransform.Position - planet.Center);

        var transferEntity = state.EntityManager.CreateEntity();
        state.EntityManager.AddComponentData(transferEntity, new PlanetTransfer
        {
            Phase = EPlanetTransferPhase.Charge,
            Timer = 0f,
            TargetPlanet = pad.TargetPlanet,
            TargetPadID = pad.TargetPadID,
            ChargeDuration = pad.ChargeDuration,
            LaunchDuration = pad.LaunchDuration,
            LandingDuration = pad.LandingDuration,
            FlightHeight = pad.FlightHeight,
            SpinSpeed = pad.SpinSpeed,
            Anchor = padTransform.Position,
            AnchorNormal = normal,
            StartPosition = player.Position,
            BaseRotation = LaunchPadMath.AlignToUp(math.forward(player.Rotation), normal),
            SpinAngle = 0f,
        });

        var gameStateEntity = SystemAPI.GetSingletonEntity<GameState>();
        state.EntityManager.SetComponentData(gameStateEntity, new GameState { State = EGameState.PlanetTransfer });

        Debug.Log($"[LaunchPad] Pad {pad.PadID} launching to {pad.TargetPlanet} (pad {pad.TargetPadID})");
    }
}

internal static class LaunchPadMath
{
    /// <summary>
    /// Rotation whose up is <paramref name="up"/> and whose forward is <paramref name="forwardHint"/>
    /// flattened onto the tangent plane (falls back to any tangent when the hint is parallel to up).
    /// </summary>
    public static quaternion AlignToUp(float3 forwardHint, float3 up)
    {
        PlanetUtils.ProjectDirectionOnSurface(forwardHint, up, out var forward);

        if (math.lengthsq(forward) < 0.5f)
        {
            float3 fallback = math.abs(up.y) < 0.9f ? math.up() : math.right();
            forward = math.normalize(math.cross(fallback, up));
        }

        return quaternion.LookRotation(forward, up);
    }

    public static float EaseOutCubic(float t) => 1f - math.pow(1f - t, 3f);

    public static float EaseInOutSine(float t) => -(math.cos(math.PI * t) - 1f) * 0.5f;

    public static float EaseOutBack(float t)
    {
        const float c1 = 1.70158f;
        const float c3 = c1 + 1f;
        return 1f + c3 * math.pow(t - 1f, 3f) + c1 * math.pow(t - 1f, 2f);
    }
}
