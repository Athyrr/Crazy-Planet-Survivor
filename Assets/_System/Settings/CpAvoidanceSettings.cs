using Unity.Mathematics;
using UnityEngine;

namespace _System.Settings
{
    /// <summary>
    /// Global tuning for local avoidance. Per-entity footprint (radius) and mass are authored on each
    /// prefab's <see cref="AvoidanceAuthoring"/>; this asset only holds the shared parameters, live-
    /// tunable in play.
    ///
    /// Grid routing is automatic by radius: entities with radius &lt;= <see cref="_fineGridMaxRadius"/>
    /// use the fine grid (its cell size is derived from that threshold, so the invariant "cell &gt;=
    /// largest combined radius" holds by construction); larger entities and all obstacles use the coarse
    /// grid. The coarse grid holds few entities, so its cell can be oversized for free — just keep it
    /// &gt;= 2 * your largest avoidance radius (biggest enemy or obstacle).
    /// </summary>
    [CreateAssetMenu(fileName = "Avoidance", menuName = "CPSettings/Avoidance")]
    public class CpAvoidanceSettings : CpSettings<CpAvoidanceSettings>
    {
        [Header("Grid routing")]
        [Tooltip("Entities whose avoidance radius is <= this use the fine grid; larger ones (and all " +
                 "obstacles) use the coarse grid.")]
        [SerializeField] private float _fineGridMaxRadius = 5f;

        [Tooltip("Multiplier applied on top of the minimum safe fine-cell size (2 * threshold). >= 1. " +
                 "Raise to trade neighbour-scan precision for fewer cells (perf).")]
        [SerializeField, Range(1f, 3f)] private float _cellSizeSafetyMult = 1.1f;

        [Tooltip("Coarse grid cell size. MUST be >= 2 * your largest avoidance radius (biggest enemy or " +
                 "obstacle). Oversize freely — the coarse grid holds few entities, so a large cell is cheap.")]
        [SerializeField] private float _coarseCellSize = 64f;

        [Header("Force")]
        [Tooltip("Penetration-force gain. Higher = firmer separation.")]
        [SerializeField] private float _forceGain = 5f;

        [Tooltip("Clamp on an entity's total steering force magnitude.")]
        [SerializeField] private float _maxSteeringForce = 5f;

        [Tooltip("Mass assigned to static obstacles. Large enough that entities are pushed fully by them " +
                 "while the obstacles never move.")]
        [SerializeField] private float _obstacleMass = 1e6f;

        /// <summary> Builds the blittable snapshot consumed by the avoidance jobs. </summary>
        public AvoidanceConfig BuildConfig()
        {
            float safety = math.max(1f, _cellSizeSafetyMult);
            float threshold = math.max(0.01f, _fineGridMaxRadius);
            float fineCell = safety * 2f * threshold;
            // The coarse grid must never be finer than the fine grid, and must clear its own occupants.
            float coarseCell = math.max(_coarseCellSize, fineCell);

            return new AvoidanceConfig
            {
                FineGridMaxRadius = threshold,
                FineCellSize = fineCell,
                CoarseCellSize = coarseCell,
                ObstacleMass = _obstacleMass,
                ForceGain = _forceGain,
                MaxSteeringForce = _maxSteeringForce
            };
        }
    }
}
