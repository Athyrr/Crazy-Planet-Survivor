using Unity.Entities;
using Unity.Mathematics;

public struct OrbitMovement : IComponentData
{
    public Entity OrbitCenterEntity;
    public float3 OrbitCenterPosition;
    public float AngularSpeed;
    public float Radius;
    public float3 RelativeOffset; // Relative position to the orbit center
    public float InitialAngle { get; set; }
}


