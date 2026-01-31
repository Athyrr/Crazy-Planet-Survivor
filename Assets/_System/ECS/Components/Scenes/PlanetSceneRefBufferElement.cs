using Unity.Entities.Serialization;
using Unity.Entities;

/// <summary>
/// Buffer element that holds a reference to a planet scene with its associated planet ID.
/// </summary>
public struct PlanetSceneRefBufferElement : IBufferElementData
{
    public EPlanetID PlanetID;

    // public SceneEntityReference SceneReference;
    public EntitySceneReference SceneReference;
}
