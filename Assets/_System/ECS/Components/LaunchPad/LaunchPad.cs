using Unity.Entities;
using Unity.Mathematics;

/// <summary>
/// A launch pad sitting on a planet. When the player steps on an armed pad during a run, the player
/// is launched into the sky (Mario Galaxy launch-star style), the target planet scene is streamed in,
/// and the player lands on the target pad (or the target planet's PlayerStart). The run carries over.
/// </summary>
public struct LaunchPad : IComponentData
{
    /// <summary>Identifier of this pad on its planet; pads on other planets use it as a landing spot.</summary>
    public int PadID;

    /// <summary>Planet scene streamed in when this pad fires.</summary>
    public EPlanetID TargetPlanet;

    /// <summary>Pad on the target planet to land on. 0 lands on the target planet's PlayerStart.</summary>
    public int TargetPadID;

    /// <summary>Distance (world units) from the pad center at which the player triggers it.</summary>
    public float TriggerRadius;

    /// <summary>Run progress ratio (0..1) required before the pad can fire. 0 = always open.</summary>
    public float UnlockAtRunProgress;

    /// <summary>Launch / landing tuning, copied into the <see cref="PlanetTransfer"/> when the pad fires.</summary>
    public float ChargeDuration;
    public float LaunchDuration;
    public float LandingDuration;
    public float FlightHeight;
    public float SpinSpeed;

    /// <summary>Raycast the pad onto the Landscape surface once the planet is loaded.</summary>
    public bool SnapToSurface;

    /// <summary>True once the pad has been placed on the surface (or does not need to be).</summary>
    public bool Placed;

    /// <summary>
    /// A pad only arms once the player has been seen outside its radius. This keeps the player from
    /// being re-launched straight away when spawning or landing on top of a pad.
    /// </summary>
    public bool Armed;

    /// <summary>Frames spent trying to place the pad (gives up after a while if no surface is found).</summary>
    public int PlaceAttempts;
}

/// <summary>
/// Decorative part of a pad (the floating star) that spins and bobs in the pad's local space.
/// </summary>
public struct LaunchPadVisual : IComponentData
{
    public float SpinSpeed;
    public float BobAmplitude;
    public float BobFrequency;
    public float3 BasePosition;
    public quaternion BaseRotation;
}

public enum EPlanetTransferPhase : byte
{
    /// <summary>Player is pulled to the pad center and winds up (spin + crouch).</summary>
    Charge,

    /// <summary>Player shoots up into the sky and vanishes.</summary>
    Launch,

    /// <summary>Target planet scene is streaming (loading screen is up).</summary>
    Loading,

    /// <summary>Player falls from the sky onto the arrival point of the new planet.</summary>
    Landing,

    /// <summary>Short squash on touchdown before control is given back.</summary>
    Touchdown,
}

/// <summary>
/// Singleton describing an in-flight planet-to-planet transfer. Lives on a runtime entity (not in a
/// planet subscene) so it survives the scene swap. Its presence keeps the game in
/// <see cref="EGameState.PlanetTransfer"/>.
/// </summary>
public struct PlanetTransfer : IComponentData
{
    public EPlanetTransferPhase Phase;
    public float Timer;

    public EPlanetID TargetPlanet;
    public int TargetPadID;

    public float ChargeDuration;
    public float LaunchDuration;
    public float LandingDuration;
    public float FlightHeight;
    public float SpinSpeed;

    /// <summary>Ground point the current phase is anchored to (pad center or arrival point).</summary>
    public float3 Anchor;

    /// <summary>Up direction at <see cref="Anchor"/> (away from the planet center).</summary>
    public float3 AnchorNormal;

    /// <summary>Player position when the transfer started (used to pull it onto the pad center).</summary>
    public float3 StartPosition;

    /// <summary>Player facing (without spin) for the current phase.</summary>
    public quaternion BaseRotation;

    /// <summary>Accumulated spin angle (radians) around <see cref="AnchorNormal"/>.</summary>
    public float SpinAngle;
}
