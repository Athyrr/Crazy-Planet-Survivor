using System.Collections.Generic;
using Unity.Entities;
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

    private class Baker : Baker<HazardZoneAuthoring>
    {
        public override void Bake(HazardZoneAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.WorldSpace);

            uint targetLayers = 0;
            if (authoring.AffectPlayer) targetLayers |= CollisionLayers.Player;
            if (authoring.AffectEnemies) targetLayers |= CollisionLayers.Enemy;

            AddComponent(entity, new HazardZone
            {
                Shape = authoring.Shape,
                Radius = authoring.Radius,
                BoxHalfExtents = authoring.BoxHalfExtents,
                TargetLayers = targetLayers,
            });

            var buffer = AddBuffer<HazardZoneEffectElement>(entity);
            if (authoring.Effects != null)
            {
                foreach (var e in authoring.Effects)
                {
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
