using System;
using Random = UnityEngine.Random;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;
using EasyButtons;

[ExecuteAlways]
public class DamageFeedbackManager : MonoBehaviour, IDisposable
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
            Destroy(gameObject);
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
    [SerializeField] private float _damageLifetime = 5f;

    private ComputeBuffer _damageBuffer;
    private ComputeBuffer _timeBuffer;

    private readonly List<DamageData> _activeDamages = new List<DamageData>();
    private readonly List<TimeData> _activeTime = new List<TimeData>();
    private bool _buffersDirty = false;

    private const int BUFFER_COUNT = 1024;

    private EntityManager _entityManager;
    private EntityQuery _damageFeedbackQuery;

    #endregion

    #region Core

    private void Start()
    {
        if (Application.isPlaying)
        {
            _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            _damageFeedbackQuery = _entityManager.CreateEntityQuery(typeof(DamageFeedbackRequest));
        }

        InitBuffer();
    }

    void Update()
    {
        if (Application.isPlaying && World.DefaultGameObjectInjectionWorld != null && _damageFeedbackQuery != null)
        {
            if (!_damageFeedbackQuery.IsEmptyIgnoreFilter)
            {
                var requests =
                    _damageFeedbackQuery.ToComponentDataArray<DamageFeedbackRequest>(Unity.Collections.Allocator.Temp);
                var entities = _damageFeedbackQuery.ToEntityArray(Unity.Collections.Allocator.Temp);

                for (int i = 0; i < entities.Length; i++)
                {
                    AddDamage(
                        requests[i].Amount,
                        (Vector3)requests[i].Transform.Position +
                        (Vector3)requests[i].Transform.Up() * (1.5f * Random.Range(1, 3f)),
                        requests[i].IsCritical ? Color.wheat : Color.firebrick
                    );
                    _entityManager.DestroyEntity(entities[i]);
                }

                requests.Dispose();
                entities.Dispose();
            }
        }

        if (_damageBuffer == null || _computeShader == null || _displayMaterial == null)
            return;

        float currentTime = GetCurrentTime();

        // Cleanup old damages
        int removedCount = 0;
        for (int i = _activeTime.Count - 1; i >= 0; i--)
        {
            if (currentTime - _activeTime[i].Time > _damageLifetime)
            {
                _activeDamages.RemoveAt(i);
                _activeTime.RemoveAt(i);
                removedCount++;
            }
        }

        if (removedCount > 0)
        {
            _buffersDirty = true;
        }

        if (_buffersDirty)
        {
            _damageBuffer.SetData(_activeDamages.ToArray());
            _timeBuffer.SetData(_activeTime.ToArray());
            _buffersDirty = false;
        }

        if (_activeDamages.Count == 0)
            return;

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
        Dispose();
    }

    public void Dispose()
    {
        ReleaseBuffer();
    }

    #endregion

    #region Methods

    private void InitBuffer()
    {
        ReleaseBuffer();

        _damageBuffer = new ComputeBuffer(BUFFER_COUNT, sizeof(float) * 7);
        _damageBuffer.SetData(new DamageData[BUFFER_COUNT]);

        _timeBuffer = new ComputeBuffer(BUFFER_COUNT, sizeof(float) * 1);
        _timeBuffer.SetData(new TimeData[BUFFER_COUNT]);
    }

    public void AddDamage(int val, Vector3 pos, Color color)
    {
        if (_damageBuffer == null)
        {
            InitBuffer();
        }

        if (_activeDamages.Count >= BUFFER_COUNT)
        {
            _activeDamages.RemoveAt(0);
            _activeTime.RemoveAt(0);
        }

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

        _buffersDirty = true;
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
        AddDamage(Random.Range(10, 999999), transform.position,
            Color.Lerp(Color.firebrick, Color.wheat, .5f));
        AddDamage(Random.Range(10, 9999), transform.position, Color.firebrick);
        AddDamage(Random.Range(10, 99), transform.position, Color.wheat);
    }

    [Button]
    void TestDamageSingle()
    {
        AddDamage(Random.Range(10, 999999), transform.position, Color.wheat);
    }

    #endregion
}