using Unity.Entities;
using UnityEngine;

public struct ExperienceLoot : IComponentData
{
    public int ExperienceValue;
    public float DropChance;
}
