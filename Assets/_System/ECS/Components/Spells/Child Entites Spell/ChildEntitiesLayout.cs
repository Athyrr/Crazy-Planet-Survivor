using Unity.Entities;
using Unity.Mathematics;

public struct ChildEntitiesLayout_Circle : IComponentData
{
    public float Radius;
    public float AngleInDegrees; // If 0 or 360 -> full circle, if less -> arc
}

public struct ChildEntitiesLayout_Line : IComponentData
{
    float3 Direction;
    public float Spacing;
}

public struct ChildEntitiesLayout_Cone : IComponentData
{
    public float Angle;
}

public struct ChildEntitiesLayout_Grid : IComponentData
{
}

public struct ChildEntitiesLayout_Random : IComponentData
{
}
