using Unity.Entities;
using Unity.Collections;

public struct GameStatistics : IComponentData
{
    public int EnemiesCreated;
    public int EnemiesKilled;
    public int UpgradesSelected;
    public int PlayerDamageTaken;
    public int SpellsCasted;
    public float TotalDamageDealt;
}
