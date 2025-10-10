using Unity.Entities;

public struct Lifetime : IComponentData
{
    public float Duration;
    public float ElapsedTime;
}
