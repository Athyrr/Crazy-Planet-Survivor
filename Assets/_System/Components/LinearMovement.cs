using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public struct LinearMovement : IComponentData
{
    public float3 Direction;
    public float Speed;
}

public struct FollowTargetMovement : IComponentData
{
    public Entity Target;
    public float Speed;
    public float StopDistance;
}

public struct OrbitMovement : IComponentData
{
    public float3 OrbitCenter;
    public float AngularSpeed;
    public float Radius;
}

public struct RotationSpeed : IComponentData
{
    public float Value;
}

// @todo Array of SpawnData struct with Prefab + ennemies base data
public struct SpawnConfig : IComponentData
{
    public Entity Prefab;
    public int Amount;
    public float DefaultSpeed;
}

public struct PlanetData : IComponentData
{
    public Entity Prefab;
    public float Radius;
}

public struct InputData : IComponentData
{
    public float2 Value;
}

public struct CameraSettings : IComponentData
{
    public UnityObjectRef<Camera> Camera;
    public float Smooth;
    public float RotationSmooth;
    public float Distance;
    public float Height;

    public float3 WorldOffset;
    public quaternion FixedRotation;
}

public struct CameraWorldData : IComponentData
{
    public float3 Position;
    public float3 Forward;
    public float3 Right;
    public float3 Up;
}