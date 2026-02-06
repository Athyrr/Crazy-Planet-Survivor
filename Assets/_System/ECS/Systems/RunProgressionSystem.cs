using Unity.Entities;
using Unity.Burst;
using UnityEngine;

/// <summary>
/// System that manages the progression timer of a run. 
/// </summary>
[UpdateInGroup(typeof(SimulationSystemGroup))]
[BurstCompile]
public partial struct RunProgressionSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<PlanetData>();
        state.RequireForUpdate<GameState>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        if (!SystemAPI.TryGetSingleton<GameState>(out GameState gameState))
            return;
        if (gameState.State != EGameState.Running)
            return;

        if (!SystemAPI.TryGetSingleton<PlanetData>(out PlanetData planetData))
            return;
        if (!SystemAPI.TryGetSingleton<RunProgression>(out var timer))
        {
            var progressionEntity = state.EntityManager.CreateEntity();
            state.EntityManager.AddComponentData(progressionEntity, new RunProgression
            {
                Timer = 0,
                ProgressRatio = 0,
                PlanetID = planetData.PlanetID
            });
        }

        if (SystemAPI.TryGetSingleton<EndRunRequest>(out var _))
            return;

        var ecbSingleton = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>();
        var ecb = ecbSingleton.CreateCommandBuffer(state.WorldUnmanaged);


        var runProgressionJob = new RunProgressionJob
        {
            ECB = ecb.AsParallelWriter(),
            DeltaTime = SystemAPI.Time.DeltaTime,
            RunDuration = planetData.RunDuration
        };
        state.Dependency = runProgressionJob.Schedule(state.Dependency);

        //Debug.Log("Planet: " + timer.PlanetID +
        //    "\nTimer: " + timer.Timer +
        //    "\n Progression: " + timer.ProgressRatio);
    }

    [BurstCompile]
    private partial struct RunProgressionJob : IJobEntity
    {
        public EntityCommandBuffer.ParallelWriter ECB;

        public float DeltaTime;
        public float RunDuration;

        public void Execute(ref RunProgression run)
        {
            run.Timer += DeltaTime;
            run.ProgressRatio = run.Timer / RunDuration;

            if (run.ProgressRatio >= 1)
            {
                run.ProgressRatio = 1;
                var endRunReqEntity = ECB.CreateEntity(0);
                ECB.AddComponent(0, endRunReqEntity, new EndRunRequest { State = EEndRunState.Timeout });
            }
        }
    }
}
