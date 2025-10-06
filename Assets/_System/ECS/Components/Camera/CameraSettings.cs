using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public struct CameraSettings : IComponentData
{
    public UnityObjectRef<Camera> Camera;
    public float Smooth;
    public float RotationSmooth;
    public float CameraAngle;
    public float CameraDistance;

    public float3 LocalOffset;
    public quaternion LocalRotation;
}
