using Unity.Entities;

public partial class PlanetInitializerSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireForUpdate<PlanetData>();
    }

    protected override void OnUpdate()
    {
        if (!SystemAPI.HasSingleton<GameStatistics>())
        {
            var entity = EntityManager.CreateEntity();
            EntityManager.AddComponentData(entity, new GameStatistics());
        }
        this.Enabled = false;
    }
}
