using UnityEngine;

namespace _System.Settings
{
    [CreateAssetMenu(fileName = "Enemy", menuName = "CPSettings/Enemy")]
    public class CpBaseEnemySettings : CpSettings<CpBaseEnemySettings>
    {
        [Header("Movement Setting")]
        [Tooltip("How fast enemies build up to their target speed, in units/s². Low = heavy and floaty, " +
                 "high = instant and twitchy. This is also what damps the avoidance jitter: a force that " +
                 "flips every tick barely moves the velocity vector, so keep it moderate rather than huge.")]
        [SerializeField, Range(1f, 200f)] private float _acceleration = 40f;

        [Tooltip("Hard cap on how fast an enemy can turn, in degrees/second. 360 = one full turn per " +
                 "second. Lower = heavier, wider curves; higher = snappier.")]
        [SerializeField, Range(30f, 1440f)] private float _maxTurnRateDeg = 540f;

        [Tooltip("Speed (units/s) below which an enemy stops steering its facing from its own velocity " +
                 "and looks at the player instead. A near-zero velocity has a meaningless direction, so " +
                 "orienting from it is exactly what makes blocked entities spin on the spot. This also " +
                 "makes an enemy stopped at attack range face its target, so its attacks stay readable.")]
        [SerializeField, Range(0f, 5f)] private float _facingSpeedThreshold = 0.5f;

        [Header("Mass influence")]
        [Tooltip("How much an entity's Avoidance mass slows its acceleration, as an exponent: " +
                 "accel *= (ReferenceMass / mass) ^ influence. 0 = mass ignored, every enemy accelerates " +
                 "the same; 1 = full inverse-mass (the physical Reynolds model, usually too brutal). " +
                 "Around 0.35 gives heavy enemies noticeable inertia without making them sluggish.")]
        [SerializeField, Range(0f, 1f)] private float _massAccelerationInfluence = 0.35f;

        [Tooltip("Same, applied to the turn rate. Kept separate from the acceleration influence because " +
                 "the two are physically distinct (mass vs moment of inertia) — a heavy enemy can start " +
                 "slowly yet still pivot well, or the opposite. Raise it above the acceleration influence " +
                 "for units that should describe wide, committed curves.")]
        [SerializeField, Range(0f, 1f)] private float _massTurnRateInfluence = 0.35f;

        [Tooltip("Mass that moves at exactly the configured Acceleration / MaxTurnRateDeg. Set it to your " +
                 "baseline trash-mob mass; anything heavier is slower, anything lighter faster.")]
        [SerializeField] private float _referenceMass = 1f;

        [Tooltip("Floor on the mass factor, for both acceleration and turn rate. Guards the degenerate " +
                 "case where a very heavy entity combined with a high influence ends up unable to move or " +
                 "turn at all (which reads as a frozen, broken enemy rather than a heavy one).")]
        [SerializeField, Range(0.01f, 1f)] private float _minMassFactor = 0.1f;

        #region Accessor

        public static float Acceleration => I._acceleration;
        public static float MaxTurnRateDeg => I._maxTurnRateDeg;
        public static float FacingSpeedThreshold => I._facingSpeedThreshold;
        public static float MassAccelerationInfluence => I._massAccelerationInfluence;
        public static float MassTurnRateInfluence => I._massTurnRateInfluence;
        public static float ReferenceMass => I._referenceMass;
        public static float MinMassFactor => I._minMassFactor;

        #endregion
    }
}
