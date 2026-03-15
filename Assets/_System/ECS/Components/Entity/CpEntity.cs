using Unity.Entities;

namespace _System.ECS.Components.Entity
{
    /// <summary>
    /// Use this to query any entity. Use CP to avoid conflicts with Unity's namespaces.
    /// </summary>
    public struct CpEntity : IComponentData 
    {
    }
}