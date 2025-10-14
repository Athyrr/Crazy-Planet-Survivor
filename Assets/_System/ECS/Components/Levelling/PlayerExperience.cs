using Unity.Entities;

public struct PlayerExperience : IComponentData
{
    public int Level;
    public float Experience;
    public int NextLevelExperienceRequired;
}
