using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>
/// Authoring for an environmental <see cref="HazardZone"/> (lava burn, slow zones, …).
/// </summary>
public class HazardZoneAuthoring : MonoBehaviour
{
    [System.Serializable]
    public struct EffectEntry
    {
        public EHazardEffectType Type;

        [Tooltip("Burn: damage per tick. Slow: speed-reduction multiplier (0.3 = -30%).")]
        public float Magnitude;

        [Tooltip("Burn: seconds between damage ticks. Unused for Slow/Stun.")]
        public float TickRate;

        [Tooltip("How long the effect keeps running after the entity leaves the zone (seconds).")]
        public float Linger;
    }

    [Header("Shape")]
    public EHazardShape Shape = EHazardShape.Sphere;

    [Tooltip("Sphere radius (Shape = Sphere).")]
    public float Radius = 3f;

    [Tooltip("Box half-extents in world axes (Shape = Box, axis-aligned).")]
    public Vector3 BoxHalfExtents = new Vector3(3f, 2f, 3f);

    [Header("Targets")]
    public bool AffectPlayer = true;
    public bool AffectEnemies = true;

    [Header("Effects applied while inside")]
    public List<EffectEntry> Effects = new()
    {
        new EffectEntry { Type = EHazardEffectType.Burn, Magnitude = 5f, TickRate = 0.5f, Linger = 1f },
    };

    [Header("Performance")]
    [Tooltip("Seconds between effect refreshes. The zone re-applies its effects at this cadence instead " +
             "of every frame. Keep every effect's Linger >= this value, or the effect flickers off between refreshes.")]
    public float RefreshInterval = 0.25f;

    private class Baker : Baker<HazardZoneAuthoring>
    {
        public override void Bake(HazardZoneAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.WorldSpace);

            uint targetLayers = 0;
            if (authoring.AffectPlayer) targetLayers |= CollisionLayers.Player;
            if (authoring.AffectEnemies) targetLayers |= CollisionLayers.Enemy;

            float refreshInterval = math.max(0.01f, authoring.RefreshInterval);
            // Deterministic staggered start phase from world position, so zones don't all refresh on the same frame.
            float3 p = authoring.transform.position;
            float phase = math.frac(math.dot(p, new float3(12.9898f, 78.233f, 37.719f)) * 43758.5453f) * refreshInterval;

            AddComponent(entity, new HazardZone
            {
                Shape = authoring.Shape,
                Radius = authoring.Radius,
                BoxHalfExtents = authoring.BoxHalfExtents,
                TargetLayers = targetLayers,
                RefreshInterval = refreshInterval,
                RefreshTimer = phase,
            });

            var buffer = AddBuffer<HazardZoneEffectElement>(entity);
            if (authoring.Effects != null)
            {
                foreach (var e in authoring.Effects)
                {
                    if (e.Linger < refreshInterval)
                        Debug.LogWarning(
                            $"[HazardZone] '{authoring.name}': effect Linger ({e.Linger}s) < RefreshInterval ({refreshInterval}s) — the effect may flicker. Increase Linger.",
                            authoring);

                    buffer.Add(new HazardZoneEffectElement
                    {
                        Type = e.Type,
                        Magnitude = e.Magnitude,
                        TickRate = e.TickRate,
                        Linger = e.Linger,
                    });
                }
            }
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.35f, 0.05f, 0.6f);
        if (Shape == EHazardShape.Sphere)
            Gizmos.DrawWireSphere(transform.position, Radius);
        else
            Gizmos.DrawWireCube(transform.position, BoxHalfExtents * 2f);
    }
}
