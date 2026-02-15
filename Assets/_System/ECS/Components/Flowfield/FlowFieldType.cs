using System;
using UnityEngine;

[Serializable]
public struct FlowFieldType 
{
    public Vector3 Position;    // default point location
    public Vector3 Forward;     // used for flow field direction

    public FlowFieldType(Vector3 position, Vector3 forward)
    {
        this.Position = position;
        this.Forward = forward;
    }
}
