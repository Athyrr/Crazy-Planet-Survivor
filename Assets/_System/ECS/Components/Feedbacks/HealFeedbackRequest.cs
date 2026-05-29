using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

/// <summary>
/// Fired via ECB when an entity (player) receives healing.
/// Consumed by <see cref="FloatingNumberFeedbackManager"/> to display a green floating heal number.
/// </summary>
public struct HealFeedbackRequest : IComponentData
{
    public int Amount;
    public LocalTransform Transform;
}
