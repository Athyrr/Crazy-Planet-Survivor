using _System.ECS.Authorings.Resources;
using Unity.Entities;

/// <summary>
/// Component identifying this orb as a resource of a specific type.
/// Replace the pattern of using DynamicBuffer&lt;ResourceBufferElement&gt; on orb prefabs:
/// the buffer is now reserved for Player and GameState entities only.
/// Differentiate from XP orbs via presence of this component vs ExperienceOrb.
/// </summary>
public struct Resource : IComponentData
{
    public EResourceType Type;
}
