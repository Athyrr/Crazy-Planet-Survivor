using Unity.Entities;

/// <summary>
/// Run timer component that keeps track of the elapsed time in a run.
/// </summary>
public struct RunProgression : IComponentData
{
    public EPlanetID PlanetID;
    public float Timer;
    /// <summary> expressed betqeen 0 and 1 not 0 and 100 </summary>
    public float ProgressRatio;
    /// <summary> Ratio of enemies killed in the current wave (0 to 1). </summary>
    public float EnemiesKilledRatio;
}
