using System;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public struct FlowFieldData : IComponentData
{
    public int2 GridSize;
    public float CellSize;
    public NativeArray<float> DistanceField; // Le "Flat Array" dont on a parlé
    public NativeArray<float2> DirectionField;
}
