using System;
using _System.ECS.Authorings.Resources;

/// <summary>
/// Represents a cost in resources of a specific type.
/// Replaces EnumValues<ERessourceType, int> for shop pricing.
/// </summary>
[Serializable]
public struct ResourceCost
{
    public EResourceType Type;
    public int Amount;
}
