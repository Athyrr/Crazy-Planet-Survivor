using Unity.Entities;
using UnityEngine;

/// <summary>
/// Buffer of all available Stats Upgrades for an character.
/// </summary>
public struct StatsUpgradePoolBufferElement : IBufferElementData
{
    public int DatabaseIndex;
}