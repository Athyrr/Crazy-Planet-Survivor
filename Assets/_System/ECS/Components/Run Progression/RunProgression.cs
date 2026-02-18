using Unity.Entities;

/// <summary>
/// Run timer component that keeps track of the elapsed time in a run.
/// </summary>
public struct RunProgression : IComponentData
{
    public EPlanetID PlanetID;
    public float Timer;
    public float ProgressRatio;
    /// <summary> Ratio of enemies killed in the current wave (0 to 1). </summary>
    public float EnemiesKilledRatio;
    public float EnemiesKilledCount;

    public float TotalDamageDealt;
    public float TotalDamageTaken;
    public float TotalExperienceCollected;
}
