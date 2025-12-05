using Unity.Entities;

public struct FollowTargetMovement : IComponentData, IEnableableComponent
{
    public Entity Target;
    public float Speed;
    public float StopDistance;
}
