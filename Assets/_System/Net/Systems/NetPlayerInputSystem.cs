using Unity.Entities;
using Unity.Mathematics;
using Unity.NetCode;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Client: samples keyboard / gamepad and writes the camera-relative world move direction into the locally owned
/// player's <see cref="NetPlayerInput"/>. Netcode then sends it to the server and replays it during prediction.
/// Only the local player is touched (GhostOwnerIsLocal) — this is the multiplayer replacement for the solo
/// PlayerInputSystem + GetSingleton&lt;InputData&gt; pattern.
/// </summary>
[UpdateInGroup(typeof(GhostInputSystemGroup))]
[WorldSystemFilter(WorldSystemFilterFlags.ClientSimulation)]
public partial class NetPlayerInputSystem : SystemBase
{
    /// <summary>When true the local player wanders on its own (used for automated bandwidth runs).</summary>
    public static bool BotInput;

    protected override void OnCreate()
    {
        RequireForUpdate<NetPlayer>();
        RequireForUpdate<PlanetData>();
    }

    protected override void OnUpdate()
    {
        float2 move = BotInput ? BotMove() : ReadDevices();
        float3 planetCenter = SystemAPI.GetSingleton<PlanetData>().Center;

        var cam = Camera.main;
        float3 camForward = cam != null ? (float3)(cam.transform.forward + cam.transform.up) : new float3(0, 0, 1);

        foreach (var (input, transform) in SystemAPI.Query<RefRW<NetPlayerInput>, RefRO<LocalTransform>>()
                     .WithAll<GhostOwnerIsLocal>())
        {
            float3 up = math.normalizesafe(transform.ValueRO.Position - planetCenter, new float3(0, 1, 0));
            PlanetUtils.ProjectDirectionOnSurface(camForward, up, out float3 forward);
            if (math.lengthsq(forward) < 0.001f)
                PlanetUtils.ProjectDirectionOnSurface(transform.ValueRO.Forward(), up, out forward);
            float3 right = math.cross(up, forward);

            input.ValueRW.MoveDirection = right * move.x + forward * move.y;
        }
    }

    private static float2 ReadDevices()
    {
        float2 move = float2.zero;
        var keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.wKey.isPressed || keyboard.upArrowKey.isPressed) move.y += 1;
            if (keyboard.sKey.isPressed || keyboard.downArrowKey.isPressed) move.y -= 1;
            if (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed) move.x += 1;
            if (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed) move.x -= 1;
        }

        var gamepad = Gamepad.current;
        if (gamepad != null)
            move += (float2)gamepad.leftStick.ReadValue();

        float len = math.length(move);
        return len > 1f ? move / len : move;
    }

    private static float2 BotMove()
    {
        float t = UnityEngine.Time.time;
        return new float2(math.sin(t * 0.7f), math.cos(t * 0.3f));
    }
}
