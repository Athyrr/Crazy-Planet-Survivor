using Unity.Entities;

public struct FollowTargetMovement : IComponentData
{
    public Entity Target;
    public float Speed;
    public float StopDistance;
}
