using Unity.Entities;
using UnityEngine;

/// <summary>
/// Managed component carrying the boss's display data for UI (name, icon).
/// Added via <c>AddComponentObject</c> in the boss baker, because strings/sprites cannot live in a Burst-compatible component.
/// </summary>
public class BossPresentation : IComponentData
{
    public string DisplayName;
    public Sprite Icon;
}
