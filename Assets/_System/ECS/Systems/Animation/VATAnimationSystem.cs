using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Rendering;
using Unity.Transforms;

/// <summary>
/// Drives every VAT instance from its own measured world movement: speed = distance / dt (teleports ignored),
/// smoothed, then VATAnimationMath.Step picks the clips, integrates the phase and writes _VATFrame.
/// Runs in presentation, after the transforms are final and before Entities Graphics uploads them.
/// </summary>
[UpdateInGroup(typeof(PresentationSystemGroup))]
[UpdateBefore(typeof(EntitiesGraphicsSystem))]
[BurstCompile]
public partial struct VATAnimationSystem : ISystem
{
    [BurstCompile]
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<VATClipTable>();
    }

    [BurstCompile]
    public void OnUpdate(ref SystemState state)
    {
        new VATAnimationJob { DeltaTime = SystemAPI.Time.DeltaTime }.ScheduleParallel();
    }

    [BurstCompile]
    private partial struct VATAnimationJob : IJobEntity
    {
        public float DeltaTime;

        private void Execute(Entity entity, ref VATFrame frame, ref VATAnimationState state, in VATClipTable clipTable, in LocalToWorld localToWorld)
        {
            ref var table = ref clipTable.Value.Value;
            float3 position = localToWorld.Position;

            if (state.Initialized == 0)
            {
                // First pass: remember the position only; each instance starts at its own phase (no synchronous herd).
                state.PrevPosition = position;
                state.Phase = Random.CreateFromIndex((uint)entity.Index).NextFloat();
                state.SmoothedSpeed = 0f;
                state.Initialized = 1;
                frame.Value = VATAnimationMath.Step(ref table, 0f, 0f, ref state.Phase);
                return;
            }

            if (DeltaTime < 1e-5f)
                return;   // paused (timeScale 0): nothing moves

            float rawSpeed = math.distance(position, state.PrevPosition) / DeltaTime;
            state.PrevPosition = position;
            if (!VATAnimationMath.IsTeleport(rawSpeed, table.TeleportSpeed))
                state.SmoothedSpeed = VATAnimationMath.SmoothSpeed(state.SmoothedSpeed, rawSpeed, DeltaTime, table.SpeedSmoothingTime);

            frame.Value = VATAnimationMath.Step(ref table, state.SmoothedSpeed, DeltaTime, ref state.Phase);
        }
    }
}
