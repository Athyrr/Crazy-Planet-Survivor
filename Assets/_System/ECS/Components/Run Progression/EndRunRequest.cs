using Unity.Entities;

public struct EndRunRequest : IComponentData
{
    public EEndRunState State;
}

public enum EEndRunState
{
    Death,
    Timeout
}
