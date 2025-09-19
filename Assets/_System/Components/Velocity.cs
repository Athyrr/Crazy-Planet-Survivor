using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public struct Velocity : IComponentData
{
    public float3 Direction;
    public float Magnitude;
}

public struct RotationSpeed : IComponentData
{
    public float Value;
}

public struct RequestForMovement : IComponentData
{
    public float3 Direction;
}

public struct SpawnConfig : IComponentData
{
    public Entity Prefab;
    public int Amount;
    public float Range;
}

public struct PlanetData : IComponentData
{
    public Entity Prefab;
    public float Radius;
}

public struct CameraTarget : IComponentData { }

public struct CameraSettings : IComponentData
{
    public UnityObjectRef<Camera> Camera;
    public float Smooth;
    public float RotationSmooth;
    public float Distance;
    public float Height;
}

public struct CameraWorldData : IComponentData
{
    public float3 Position;
    public float3 Forward;
    public float3 Right;
}