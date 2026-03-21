using Unity.Collections;
using Unity.Entities;

public struct PlayerRessources : IComponentData
{
    public FixedList128Bytes<int> Ressources; // index = enum
}
