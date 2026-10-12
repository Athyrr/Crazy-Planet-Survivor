using Unity.Entities;
using UnityEngine;

/// <summary>
/// Place on a planet (inside its planet scene) to create a launch pad. Stepping on it during a run
/// launches the player to <see cref="_targetPlanet"/> and lands them on the pad whose
/// <see cref="_padID"/> equals <see cref="_targetPadID"/> there (or on that planet's PlayerStart when
/// the target pad ID is 0). Two pads pointing at each other make a round trip.
/// </summary>
public class LaunchPadAuthoring : MonoBehaviour
{
    [Header("Link")]
    [Tooltip("Identifier of this pad on its planet. Pads on other planets land on it through their Target Pad ID. 0 = not a landing spot.")]
    [SerializeField] private int _padID = 1;

    [Tooltip("Planet the player is sent to.")]
    [SerializeField] private EPlanetID _targetPlanet = EPlanetID.None;

    [Tooltip("Pad ID to land on, on the target planet. 0 = land on the target planet's PlayerStart.")]
    [SerializeField] private int _targetPadID = 1;

    [Header("Trigger")]
    [Tooltip("Distance from the pad center at which the player is launched.")]
    [SerializeField] private float _triggerRadius = 2.5f;

    [Tooltip("Run progress (0..1) required before the pad works. 0 = always open.")]
    [Range(0f, 1f)]
    [SerializeField] private float _unlockAtRunProgress = 0f;

    [Tooltip("Raycast the pad onto the planet surface when the planet loads (recommended for procedural planets).")]
    [SerializeField] private bool _snapToSurface = true;

    [Header("Launch animation")]
    [Tooltip("Wind-up time on the pad (spin + crouch) before take-off.")]
    [SerializeField] private float _chargeDuration = 0.45f;

    [Tooltip("Time to shoot up into the sky before the next planet loads.")]
    [SerializeField] private float _launchDuration = 1.1f;

    [Tooltip("Time to fall from the sky onto the arrival point.")]
    [SerializeField] private float _landingDuration = 1.2f;

    [Tooltip("Height above the surface the player flies to / falls from.")]
    [SerializeField] private float _flightHeight = 70f;

    [Tooltip("Peak spin speed of the player (turns per second).")]
    [SerializeField] private float _spinTurnsPerSecond = 3f;

    public int PadID => _padID;
    public EPlanetID TargetPlanet => _targetPlanet;
    public int TargetPadID => _targetPadID;
    public float TriggerRadius => _triggerRadius;

    private class Baker : Baker<LaunchPadAuthoring>
    {
        public override void Bake(LaunchPadAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new LaunchPad
            {
                PadID = authoring._padID,
                TargetPlanet = authoring._targetPlanet,
                TargetPadID = authoring._targetPadID,
                TriggerRadius = Mathf.Max(0.1f, authoring._triggerRadius),
                UnlockAtRunProgress = authoring._unlockAtRunProgress,
                ChargeDuration = Mathf.Max(0f, authoring._chargeDuration),
                LaunchDuration = Mathf.Max(0.05f, authoring._launchDuration),
                LandingDuration = Mathf.Max(0.05f, authoring._landingDuration),
                FlightHeight = Mathf.Max(1f, authoring._flightHeight),
                SpinSpeed = authoring._spinTurnsPerSecond * Mathf.PI * 2f,
                SnapToSurface = authoring._snapToSurface,
                Placed = !authoring._snapToSurface,
                Armed = false,
                PlaceAttempts = 0,
            });
        }
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = _targetPlanet == EPlanetID.None ? Color.gray : new Color(1f, 0.85f, 0.2f);
        Gizmos.DrawWireSphere(transform.position, _triggerRadius);
        Gizmos.DrawLine(transform.position, transform.position + transform.up * 4f);
    }
}
