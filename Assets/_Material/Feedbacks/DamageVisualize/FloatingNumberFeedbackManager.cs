using System.Collections.Generic;
using EasyButtons;
using PrimeTween;
using Unity.Entities;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif
using Random = UnityEngine.Random;

[ExecuteAlways]
public class FloatingNumberFeedbackManager : MonoBehaviour
{
    [Header("Damage Colors")] public Color BaseDamageColor = Color.white;
    public Color CriticalDamageColor = Color.goldenRod;
    public Color BurnDamageColor = Color.darkRed;

    [Header("Heal Colors")] public Color HealColor = Color.green;

    #region Instance

    private static FloatingNumberFeedbackManager _instance;

    public static FloatingNumberFeedbackManager Instance
    {
        get
        {
            if (_instance == null)
            {
                _instance = FindAnyObjectByType<FloatingNumberFeedbackManager>();
                if (_instance == null)
                {
                    var go = new GameObject("FloatingNumberFeedbackManager");
                    _instance = go.AddComponent<FloatingNumberFeedbackManager>();
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

    private struct DamageData
    {
        public Vector3 Position;
        public float Value;
        public float StartTime;
        public int DigitCount;
        public Color Color;
    }

    [SerializeField] public ComputeShader computeShader;

    [SerializeField] public Material displayMaterial;

    [SerializeField] public Mesh quadMesh;

    private ComputeBuffer _damageBuffer;
    private List<DamageData> _activeDamages = new();
    private const int _maxNumbers = 50;

    private EntityManager _entityManager;
    private EntityQuery _damageFeedbackQuery;
    private EntityQuery _healFeedbackQuery;
    private World _lastWorld;

    private void Start()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
            return;

        _entityManager = world.EntityManager;
        _damageFeedbackQuery = _entityManager.CreateEntityQuery(typeof(DamageFeedbackRequest));
        _healFeedbackQuery = _entityManager.CreateEntityQuery(typeof(HealFeedbackRequest));
        _lastWorld = world;

        InitBuffer();
    }

    private void InitBuffer()
    {
        if (_damageBuffer == null)
        {
            _damageBuffer = new ComputeBuffer(_maxNumbers, sizeof(float) * 10);
            _damageBuffer.SetData(new DamageData[_maxNumbers]);
        }
    }

    public void AddFeedback(int val, Vector3 pos, Color color)
    {
        InitBuffer();

        if (_activeDamages.Count >= _maxNumbers)
            _activeDamages.RemoveAt(0);

        _activeDamages.Add(
            new DamageData
            {
                Position = pos,
                Value = val,
                StartTime = GetCurrentTime(),
                DigitCount = val.ToString().Length,
                Color = color,
            }
        );

        _damageBuffer.SetData(_activeDamages.ToArray());
    }

    private static float GetCurrentTime()
    {
        return Application.isPlaying ? Time.time : Time.realtimeSinceStartup;
    }

    private void Update()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
            return;

#if UNITY_EDITOR
        if (!EditorApplication.isPlaying && world != _lastWorld)
        {
            _entityManager = world.EntityManager;
            DisposeQueries();
            _damageFeedbackQuery = _entityManager.CreateEntityQuery(typeof(DamageFeedbackRequest));
            _healFeedbackQuery = _entityManager.CreateEntityQuery(typeof(HealFeedbackRequest));
            _lastWorld = world;
        }
#endif

        // Process damage feedback
        if (!_damageFeedbackQuery.IsEmpty)
        {
            var requests = _damageFeedbackQuery.ToComponentDataArray<DamageFeedbackRequest>(
                Unity.Collections.Allocator.Temp
            );
            var entities = _damageFeedbackQuery.ToEntityArray(Unity.Collections.Allocator.Temp);

            for (int i = 0; i < entities.Length; i++)
            {
                var req = requests[i];

                var color = BaseDamageColor;

                if (req.IsCritical)
                    color = CriticalDamageColor;
                else if (req.IsBurn)
                    color = BurnDamageColor;

                AddFeedback(
                    req.Amount,
                    (Vector3)req.Transform.Position
                    + (Vector3)req.Transform.Up() * (1.5f * Random.Range(1, 3f)),
                    color
                );

                _entityManager.DestroyEntity(entities[i]);
            }

            requests.Dispose();
            entities.Dispose();
        }

        // Process heal feedback
        if (!_healFeedbackQuery.IsEmpty)
        {
            var healRequests = _healFeedbackQuery.ToComponentDataArray<HealFeedbackRequest>(
                Unity.Collections.Allocator.Temp
            );
            var healEntities = _healFeedbackQuery.ToEntityArray(Unity.Collections.Allocator.Temp);

            for (int i = 0; i < healEntities.Length; i++)
            {
                var req = healRequests[i];

                AddFeedback(
                    req.Amount,
                    (Vector3)req.Transform.Position
                    + (Vector3)req.Transform.Up() * (1.5f * Random.Range(1, 3f)),
                    HealColor
                );

                _entityManager.DestroyEntity(healEntities[i]);
            }

            healRequests.Dispose();
            healEntities.Dispose();
        }

        if (_damageBuffer == null || computeShader == null || displayMaterial == null)
            return;

        if (_activeDamages.Count == 0)
            return;

        float currentTime = GetCurrentTime();

        int kernel = computeShader.FindKernel("UpdateNumbers");
        computeShader.SetBuffer(kernel, "_DamageBuffer", _damageBuffer);
        computeShader.SetFloat("_Time", currentTime);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(_maxNumbers / 64f), 1, 1);

        displayMaterial.SetBuffer("_DamageBuffer", _damageBuffer);
        displayMaterial.SetFloat("_CurrentTime", currentTime);

        Graphics.DrawMeshInstancedProcedural(
            quadMesh,
            0,
            displayMaterial,
            new Bounds(Vector3.zero, Vector3.one * 1000),
            _activeDamages.Count
        );
    }

    private void DisposeQueries()
    {
        if (_damageFeedbackQuery != default)
            _damageFeedbackQuery.Dispose();

        if (_healFeedbackQuery != default)
            _healFeedbackQuery.Dispose();
    }

    private void OnDestroy()
    {
        DisposeBuffer();
        DisposeQueries();
    }

    private void DisposeBuffer()
    {
        if (_damageBuffer != null)
        {
            _damageBuffer.Release();
            _damageBuffer = null;
        }
    }

    [Button]
    void TestFeedback()
    {
        var color = Color.white;

        Sequence
            .Create(cycles: 10, Sequence.SequenceCycleMode.Yoyo)
            .ChainCallback(() => AddFeedback(Random.Range(10, 999999), transform.position, color))
            .ChainDelay(0.1f)
            .ChainCallback(() => AddFeedback(Random.Range(10, 9999), transform.position, color))
            .ChainDelay(0.1f)
            .ChainCallback(() => AddFeedback(Random.Range(10, 99), transform.position, color));
    }

    [Button]
    void TestFeedbackSingle()
    {
        AddFeedback(Random.Range(10, 999999), transform.position, Color.white);
    }
}