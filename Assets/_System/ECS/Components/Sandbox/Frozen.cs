using Unity.Entities;

/// <summary>Own movement frozen (FlowFieldMovementSystem, EntitiesMovementSystem follow job, player input). Casts,
/// knockback and avoidance are untouched. enemies_freeze, the dummy (baked) and `measure &lt;s&gt; freeze` (player).</summary>
public struct Frozen : IComponentData { }
