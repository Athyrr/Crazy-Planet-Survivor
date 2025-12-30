using Unity.Entities;

public partial class PlanetInitializerSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireForUpdate<PlanetData>();
    }

    protected override void OnUpdate()
    {
#if ENABLE_STATISTICS
        if (!SystemAPI.HasSingleton<GameStatistics>())
        {
            var entity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(entity, new GameStatistics());
        }
#endif
        this.Enabled = false;
    }
}
