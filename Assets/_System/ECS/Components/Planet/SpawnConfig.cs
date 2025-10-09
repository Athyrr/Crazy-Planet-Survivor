using Unity.Entities;
// @todo Array of SpawnData struct with Prefab + ennemies base data
public struct SpawnConfig : IComponentData
{
    public Entity Prefab;
    public int Amount;
}
