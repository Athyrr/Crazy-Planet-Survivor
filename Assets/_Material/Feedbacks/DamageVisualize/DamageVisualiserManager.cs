using Random = UnityEngine.Random;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using EasyButtons;
using PrimeTween;

[ExecuteAlways]
public class DamageFeedbackManager : MonoBehaviour
{
    #region Instance

    private static DamageFeedbackManager _instance;

    public static DamageFeedbackManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindAnyObjectByType<DamageFeedbackManager>();
                if (_instance == null)
                {
                    var go = new GameObject("DamageManager");
                    _instance = go.AddComponent<DamageFeedbackManager>();
                }
            }

            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance == null)
            _instance = this;
        else if (_instance != this)
        {
            Destroy(gameObject);
            return;
        }
    }

    #endregion

    #region Struct
    struct DamageData
    {
        public Vector3 Position;
        public Vector3 Color;
        public float Value;
    }
    
    struct TimeData
    {
        public float Time;
    }
    #endregion

    #region Members
    [SerializeField] private ComputeShader _computeShader;
    [SerializeField] private Material _displayMaterial;
    [SerializeField] private Mesh _quadMesh;
    
    private ComputeBuffer _damageBuffer;
    private ComputeBuffer _timeBuffer;
    
    // sync List (tim cook me again like a shader)
    private List<DamageData> _activeDamages = new List<DamageData>();
    private List<TimeData> _activeTime = new List<TimeData>();
    
    private const int BUFFER_COUNT = 1024;

    private EntityManager _entityManager;
    private EntityQuery _damageFeedbackQuery;
    #endregion

    #region Core
    private void Start()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        _damageFeedbackQuery = _entityManager.CreateEntityQuery(typeof(DamageFeedbackRequest));

        InitBuffer();
    }
    
    void Update()
    {
        if (World.DefaultGameObjectInjectionWorld != null)
        {
            var entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            var requestQuery = entityManager.CreateEntityQuery(typeof(DamageFeedbackRequest));

            if (!requestQuery.IsEmpty)
            {
                var requests =
                    requestQuery.ToComponentDataArray<DamageFeedbackRequest>(Unity.Collections.Allocator.Temp);
                var entities = requestQuery.ToEntityArray(Unity.Collections.Allocator.Temp);

                for (int i = 0; i < entities.Length; i++)
                {
                    AddDamage(
                        requests[i].Amount,
                        (Vector3)requests[i].Transform.Position +
                        (Vector3)requests[i].Transform.Up() * 1.5f * Random.Range(1, 3f),
                        Color.Lerp(Color.wheat, Color.firebrick, requests[i].CritIntensity)
                    );
                    entityManager.DestroyEntity(entities[i]);
                }
                
                requests.Dispose();
                entities.Dispose();
            }
        }

        if (_damageBuffer == null || _computeShader == null || _displayMaterial == null)
            return;

        if (_activeDamages.Count == 0)
            return;

        float currentTime = GetCurrentTime();

        int kernel = _computeShader.FindKernel("UpdateNumbers");
        _computeShader.SetBuffer(kernel, "_DamageBuffer", _damageBuffer);
        _computeShader.SetBuffer(kernel, "_TimeBuffer", _timeBuffer);
        _computeShader.SetFloat("_CurrentTime", currentTime);
        _computeShader.Dispatch(kernel, Mathf.CeilToInt(BUFFER_COUNT / 64f), 1, 1);

        _displayMaterial.SetBuffer("_DamageBuffer", _damageBuffer);
        _displayMaterial.SetBuffer("_TimeBuffer", _timeBuffer);
        _displayMaterial.SetFloat("_CurrentTime", currentTime);

        Graphics.DrawMeshInstancedProcedural(_quadMesh, 0, _displayMaterial,
            new Bounds(Vector3.zero, Vector3.one * 1000), _activeDamages.Count);
    }

    private void OnDestroy()
    {
        ReleaseBuffer();
    }
    #endregion

    #region Methods
    private void InitBuffer()
    {
        // DamageData = Vector3(3) + Vector3(3) + float(1) = 7 floats (28 bytes)
        _damageBuffer = new ComputeBuffer(BUFFER_COUNT, sizeof(float) * 7);
        _damageBuffer.SetData(new DamageData[BUFFER_COUNT]);
        
        // TimeData = float(1) = 1 float (4 bytes)
        _timeBuffer = new ComputeBuffer(BUFFER_COUNT, sizeof(float) * 1);
        _timeBuffer.SetData(new TimeData[BUFFER_COUNT]);
    }

    public void AddDamage(int val, Vector3 pos, Color color)
    {
        InitBuffer();

        if (_activeDamages.Count >= BUFFER_COUNT)
        {
            _activeDamages.RemoveAt(0);
            _activeTime.RemoveAt(0);
        }

        var index = _activeDamages.Count;

        _activeDamages.Add(new DamageData
        {
            Position = pos,
            Color = new Vector3(color.r, color.g, color.b),
            Value = (float)val
        });
        
        _activeTime.Add(new TimeData
        {
            Time = GetCurrentTime()
        });

        _damageBuffer.SetData(_activeDamages.ToArray());
        _timeBuffer.SetData(_activeTime.ToArray());

        Sequence.Create()
            .ChainDelay(5f)
            .ChainCallback(() =>
            {
                if (index < _activeDamages.Count)
                {
                    _activeDamages.RemoveAt(index);
                    _activeTime.RemoveAt(index);
                }
            });
    }

    private float GetCurrentTime()
    {
        return Application.isPlaying ? Time.time : (float)Time.realtimeSinceStartup;
    }

    #endregion

    #region ReleaseBuffer

    private void ReleaseBuffer()
    {
        ReleaseBuffer(ref _damageBuffer);
        ReleaseBuffer(ref _timeBuffer);
    }

    private void ReleaseBuffer(ref ComputeBuffer buffer)
    {
        if (buffer != null)
        {
            buffer.Release();
            buffer = null;
        }
    }

    #endregion

    #region Editor

    [Button]
    void TestDamage()
    {
        Sequence.Create(cycles: 10, Sequence.SequenceCycleMode.Yoyo)
            .ChainCallback(() => AddDamage(Random.Range(10, 999999), transform.position, Color.Lerp(Color.firebrick, Color.wheat, .5f)))
            .ChainDelay(0.1f)
            .ChainCallback(() => AddDamage(Random.Range(10, 9999), transform.position, Color.firebrick))
            .ChainDelay(0.1f)
            .ChainCallback(() => AddDamage(Random.Range(10, 99), transform.position, Color.wheat));
    }

    [Button]
    void TestDamageSingle()
    {
        AddDamage(Random.Range(10, 999999), transform.position, Color.wheat);
    }
    
    #endregion
}
