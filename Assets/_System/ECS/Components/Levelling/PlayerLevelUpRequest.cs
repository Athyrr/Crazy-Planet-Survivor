using Unity.Entities;

public struct PlayerLevelUpRequest : IComponentData
{
    public int PendingLevels;
}
