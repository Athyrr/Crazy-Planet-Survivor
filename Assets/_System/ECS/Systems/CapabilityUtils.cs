using Unity.Entities;

public static class CapabilityUtils
{
    /// <summary>A capability is active when the slot exists on the entity AND its enableable bit is on.</summary>
    public static bool IsCapabilityActive<T>(ComponentLookup<T> lookup, Entity entity)
        where T : unmanaged, IComponentData, IEnableableComponent
        => lookup.HasComponent(entity) && lookup.IsComponentEnabled(entity);
}
