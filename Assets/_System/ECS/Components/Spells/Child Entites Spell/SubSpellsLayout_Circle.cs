using Unity.Entities;
using Unity.Mathematics;

public struct SubSpellsLayout_Circle : IComponentData
{
    public float Radius;
    public float AngleInDegrees; // If 0 or 360 -> full circle, if less -> arc
}

public struct SubSpellsLayout_Line : IComponentData
{
    float3 Direction;
    public float Spacing;
}

public struct SubSpellsLayout_Cone : IComponentData
{
    public float Angle;
}

public struct SubSpellsLayout_Grid : IComponentData
{
}

public struct SubSpellsLayout_Random : IComponentData
{
}
