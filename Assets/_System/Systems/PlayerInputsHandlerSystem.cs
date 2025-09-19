using Unity.Burst;
using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;

[UpdateInGroup(typeof(SimulationSystemGroup))]
public partial struct PlayerInputsHandlerSystem : ISystem
{

    [BurstCompile]
    private void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<Player>();
        state.RequireForUpdate<PlayerInputData>();
    }

    //[BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingletonEntity<Player>(out Entity playerEntity))
            return;


        //PlayerInputData input = SystemAPI.GetComponentRO<PlayerInputData>(playerEntity).ValueRO;

        float xValue = Input.GetAxisRaw("Horizontal");
        float yValue = Input.GetAxisRaw("Vertical");

        float2 move = new float2(xValue, yValue);

        SystemAPI.SetComponent(playerEntity, new PlayerInputData()
        {
            Value = move,
        });
    }
}
