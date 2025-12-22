using Unity.Entities;

public struct CharacterPrefabBufferElement : IBufferElementData
{
    public int CharacterIndex;

    public Entity CharacterPrefabEntity;
}
