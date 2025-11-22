// FoliageRenderer.cs
using UnityEngine;

[ExecuteAlways]
public class FoliageRenderer : MonoBehaviour
{
    public FoliageData data;
    public Mesh mesh;
    public Material material;
    public Bounds renderBounds = new Bounds(Vector3.zero, Vector3.one * 1000f);
    ComputeBuffer instanceBuffer;
    ComputeBuffer argsBuffer;

    // Struct = float3 pos + float3 normal + float scale + float3 rotation (float4 mtn flemme de refaire les maths)
    // GPU stride = 48 bytes (float3 aligned on 16 bytes)
    const int STRIDE = 40;

    void OnEnable()
    {
        CreateBuffers();
    }

    void OnDisable()
    {
        ReleaseBuffers();
    }

    void Update()
    {
        if (data == null || mesh == null || material == null) return;

        // if count changed, recreate
        if (instanceBuffer == null || instanceBuffer.count != data.instances.Count)
        {
            CreateBuffers();
        }

        if (instanceBuffer != null && data.instances.Count > 0)
        {
            instanceBuffer.SetData(data.instances);
            material.SetBuffer("_Instances", instanceBuffer);

            uint[] args = new uint[5];
            args[0] = (uint)mesh.GetIndexCount(0);
            args[1] = (uint)data.instances.Count;
            args[2] = (uint)mesh.GetIndexStart(0);
            args[3] = (uint)mesh.GetBaseVertex(0);
            args[4] = 0;
            argsBuffer.SetData(args);

            Graphics.DrawMeshInstancedIndirect(mesh, 0, material, renderBounds, argsBuffer);
        }
    }

    void CreateBuffers()
    {
        ReleaseBuffers();
        if (data == null || data.instances.Count == 0) return;

        instanceBuffer = new ComputeBuffer(data.instances.Count, STRIDE, ComputeBufferType.Default);
        instanceBuffer.SetData(data.instances);

        argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        uint[] args = new uint[5];
        args[0] = (uint)mesh.GetIndexCount(0);
        args[1] = (uint)data.instances.Count;
        args[2] = (uint)mesh.GetIndexStart(0);
        args[3] = (uint)mesh.GetBaseVertex(0);
        args[4] = 0;
        argsBuffer.SetData(args);

        if (material != null)
            material.SetBuffer("_Instances", instanceBuffer);
    }

    void ReleaseBuffers()
    {
        if (instanceBuffer != null) { instanceBuffer.Release(); instanceBuffer = null; }
        if (argsBuffer != null) { argsBuffer.Release(); argsBuffer = null; }
    }

    void OnValidate()
    {
        // optional: auto size bounds around data
        if (data != null && data.instances.Count > 0)
        {
            Vector3 center = Vector3.zero;
            foreach (var i in data.instances) center += i.position;
            center /= data.instances.Count;
            renderBounds.center = center;
            float maxDist = 0f;
            foreach (var i in data.instances) maxDist = Mathf.Max(maxDist, (i.position - center).magnitude);
            renderBounds.size = Vector3.one * (maxDist * 2f + 10f);
        }
    }
}
