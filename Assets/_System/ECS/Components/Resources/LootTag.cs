using Unity.Entities;

/// <summary>
/// Marker component for all collectible orbs (both XP and resources).
/// Used by the attraction/collection system to match orbs without relying on DynamicBuffer.
/// Differentiate orb type by checking for ExperienceOrb or Resource component.
/// </summary>
public struct LootTag : IComponentData { }
