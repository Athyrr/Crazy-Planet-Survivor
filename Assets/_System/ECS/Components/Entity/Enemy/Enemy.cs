using Unity.Entities;

public struct Enemy : IComponentData 
{
    /// <summary>
    /// The wave index this enemy belongs to. Used for tracking wave progress.
    /// </summary>
    public int WaveIndex;
}