using Unity.Entities;

//[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct RotatingCubeSystem : ISystem
{

    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<RotationSpeed>();
    }

    public void OnUpdate(ref SystemState state)
    {
        //foreach ((RefRW<LocalTransform>, RefRO<RotationSpeed>) rs in SystemAPI.Query<RefRW<LocalTransform>, RefRO<RotationSpeed>>())
        //{
        //    rs.Item1.ValueRW = rs.Item1.ValueRW.RotateY(rs.Item2.ValueRO.Value * SystemAPI.Time.DeltaTime);
        //}        
    }
}
