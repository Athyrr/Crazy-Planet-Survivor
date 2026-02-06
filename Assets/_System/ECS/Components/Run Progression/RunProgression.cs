using Unity.Entities;

/// <summary>
/// Run timer component that keeps track of the elapsed time in a run.
/// </summary>
public struct RunProgression : IComponentData
{
    public EPlanetID PlanetID;
    public float Timer;
    public float ProgressRatio;
}
