using EasyButtons;
using Unity.Entities;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
public class FoliageRenderer : MonoBehaviour
{
    #region Members
    [SerializeField] private EnumValues<EPlanetID, FoliageData> datas;
    [SerializeField] private Mesh _mesh;
    [SerializeField] private Material _material;
    [SerializeField] private Bounds _renderBounds = new Bounds(Vector3.zero, Vector3.one * 1000f);

    private FoliageData _data;
    
    private ComputeBuffer instanceBuffer;
    private ComputeBuffer argsBuffer;
    
    private EntityManager _entityManager;
    private EntityQuery _planetScenesBufferQuery;
    
    // Struct = float3 pos + float3 normal + float scale + float3 rotation (float4 mtn flemme de refaire les maths)
    // GPU stride = 48 bytes (float3 aligned on 16 bytes)
    const int STRIDE = 40;
    #endregion

    #region Core
    void OnEnable()
    {
        CreateBuffers();

        if (GameManager.Instance != null)
            GameManager.Instance.OnPlanetSelected += OnGameStateChanged;
    }

    void OnDisable()
    {
        ReleaseBuffers();
        
        if (GameManager.Instance != null)
            GameManager.Instance.OnPlanetSelected -= OnGameStateChanged;
    }

    void Update()
    {
        if (_data == null || _mesh == null || _material == null) return;

        // if count changed, recreate
        if (instanceBuffer == null || instanceBuffer.count != _data.instances.Count)
        {
            CreateBuffers();
        }

        if (instanceBuffer != null && _data.instances.Count > 0)
        {
            instanceBuffer.SetData(_data.instances);
            _material.SetBuffer("_Instances", instanceBuffer);

            uint[] args = new uint[5];
            args[0] = (uint)_mesh.GetIndexCount(0);
            args[1] = (uint)_data.instances.Count;
            args[2] = (uint)_mesh.GetIndexStart(0);
            args[3] = (uint)_mesh.GetBaseVertex(0);
            args[4] = 0;
            argsBuffer.SetData(args);

            Graphics.DrawMeshInstancedIndirect(_mesh, 0, _material, _renderBounds, argsBuffer);
        }
    }

    private void OnGameStateChanged(EPlanetID planetID)
    {
        ReleaseBuffers();
        
        _data = datas[planetID];
        
        CreateBuffers();
    }
    #endregion

    #region Methods
    void CreateBuffers()
    {
        ReleaseBuffers();
        if (_data == null || _data.instances.Count == 0) return;

        instanceBuffer = new ComputeBuffer(_data.instances.Count, STRIDE, ComputeBufferType.Default);
        instanceBuffer.SetData(_data.instances);

        argsBuffer = new ComputeBuffer(1, 5 * sizeof(uint), ComputeBufferType.IndirectArguments);
        uint[] args = new uint[5];
        args[0] = (uint)_mesh.GetIndexCount(0);
        args[1] = (uint)_data.instances.Count;
        args[2] = (uint)_mesh.GetIndexStart(0);
        args[3] = (uint)_mesh.GetBaseVertex(0);
        args[4] = 0;
        argsBuffer.SetData(args);

        if (_material != null)
            _material.SetBuffer("_Instances", instanceBuffer);
    }

    void ReleaseBuffers()
    {
        if (instanceBuffer != null) { instanceBuffer.Release(); instanceBuffer = null; }
        if (argsBuffer != null) { argsBuffer.Release(); argsBuffer = null; }
    }
    #endregion

    #region Utils
    float GetPlanetRadiusWorld(MeshFilter mf)
    {
        if (mf == null) return 1f;
        var mesh = mf.sharedMesh;
        float max = 0f;
        foreach (var v in mesh.vertices) max = Mathf.Max(max, v.magnitude);
        return max * transform.lossyScale.x;
    }
    #endregion

#if UNITY_EDITOR
    #region Editor
    void OnValidate()
    {
        // optional: auto size bounds around data
        if (_data != null && _data.instances.Count > 0)
        {
            Vector3 center = Vector3.zero;
            foreach (var i in _data.instances) center += i.position;
            center /= _data.instances.Count;
            _renderBounds.center = center;
            float maxDist = 0f;
            foreach (var i in _data.instances) maxDist = Mathf.Max(maxDist, (i.position - center).magnitude);
            _renderBounds.size = Vector3.one * (maxDist * 2f + 10f);
        }
    }
    
    [Button]
    private void RecalculateBounds(MeshFilter meshFilter)
    {
        if (meshFilter == null && !TryGetComponent<MeshFilter>(out meshFilter))
        {
            Debug.LogError("any mesh filter found. please place this script on mesh filter of the planet.");
            return;
        }
        
        var minBounds = GetPlanetRadiusWorld(meshFilter);
        meshFilter.mesh.bounds = new Bounds(transform.position, new Vector3(minBounds, minBounds, minBounds));
        EditorUtility.SetDirty(meshFilter.mesh);
    }
    #endregion
#endif
}
