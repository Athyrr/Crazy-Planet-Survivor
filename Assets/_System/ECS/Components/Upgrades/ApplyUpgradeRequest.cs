using Unity.Entities;

public struct ApplyUpgradeRequest : IComponentData
{
    public int DatabaseIndex;

    /// <summary>Console give_upgrade: keep the open upgrade choices on screen (the UI pick leaves it false and clears them).</summary>
    public bool KeepSelection;
}
