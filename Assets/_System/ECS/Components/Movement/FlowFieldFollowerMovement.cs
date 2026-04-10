using Unity.Entities;

/// <summary>
/// Tag component that opts an entity into flow-field-based navigation.
/// Entities with this component ignore the direct-to-target FollowTargetMovement
/// and instead sample the FlowFieldData singleton to determine their movement direction.
/// Requires HardSnappedMovement to also be present for terrain snapping.
/// </summary>
public struct FlowFieldFollowerMovement : IComponentData, IEnableableComponent { }
