using Unity.Entities;

public partial class PlanetInitializerSystem : SystemBase
{
    protected override void OnCreate()
    {
        RequireForUpdate<PlanetData>();
    }

    protected override void OnUpdate()
    {
        this.Enabled = false;
    }
}
