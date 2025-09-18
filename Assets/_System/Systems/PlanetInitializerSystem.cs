using Unity.Entities;
using UnityEngine;

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
