using System;
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
public class FloatingNumberFeedbackManager : MonoBehaviour, IDisposable
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

    // Reused upload buffer + dirty flag so we never allocate per hit and only push to the GPU
    // once per frame, and only when the set actually changed.
    private DamageData[] _uploadScratch;
    private bool _bufferDirty;

    // Numbers are invisible past their fade lifetime (material "_LifeTime"); we prune the CPU
    // list at that age so the dispatch/draw count tracks the *visible* numbers instead of pinning
    // at _maxNumbers. Defaults to the compute shader's 2s hide threshold if the material has none.
    private float _pruneLifetime = 2f;

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
        CachePruneLifetime();
    }

    // Derive the prune threshold from the material's fade lifetime (a number is fully transparent
    // past "_LifeTime"), with a small margin. Falls back to the compute shader's 2s hide window.
    private void CachePruneLifetime()
    {
        float matLife = displayMaterial != null ? displayMaterial.GetFloat("_LifeTime") : 0f;
        _pruneLifetime = matLife > 0.01f ? matLife + 0.15f : 2f;
    }

    public void Dispose()
    {
        DisposeQueries();
        DisposeBuffer();
    }

    private void InitBuffer()
    {
        if (_uploadScratch == null)
            _uploadScratch = new DamageData[_maxNumbers];

        if (_damageBuffer == null)
        {
            _damageBuffer = new ComputeBuffer(_maxNumbers, sizeof(float) * 10);
            _damageBuffer.SetData(new DamageData[_maxNumbers]);
            _bufferDirty = false;
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
                DigitCount = CountDigits(val),
                Color = color,
            }
        );

        // Defer the GPU upload to Update (single SetData per frame), instead of re-uploading
        // the whole buffer on every hit.
        _bufferDirty = true;
    }

    // Allocation-free digit count (val.ToString().Length allocated a string per hit).
    // Clamped to the shader's MAX_DIGITS (6).
    private static int CountDigits(int value)
    {
        if (value < 0) value = -value;
        if (value < 10) return 1;
        if (value < 100) return 2;
        if (value < 1000) return 3;
        if (value < 10000) return 4;
        if (value < 100000) return 5;
        return 6;
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
        // if (!Application.isPlaying && world != _lastWorld)
        // {
        //     _entityManager = world.EntityManager;
        //     DisposeQueries();
        //     _damageFeedbackQuery = _entityManager.CreateEntityQuery(typeof(DamageFeedbackRequest));
        //     _healFeedbackQuery = _entityManager.CreateEntityQuery(typeof(HealFeedbackRequest));
        //     _lastWorld = world;
        // }
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

        float currentTime = GetCurrentTime();

        // Time-prune: entries past their fade lifetime are invisible. The list is ordered by
        // StartTime (ascending, since GetCurrentTime is monotonic), so all expired entries are a
        // prefix — drop them in one RemoveRange. This keeps the dispatch/draw count proportional
        // to the *visible* numbers instead of staying pinned at the cap for the rest of the run.
        int dead = 0;
        while (dead < _activeDamages.Count && currentTime - _activeDamages[dead].StartTime > _pruneLifetime)
            dead++;
        if (dead > 0)
        {
            _activeDamages.RemoveRange(0, dead);
            _bufferDirty = true;
        }

        int count = _activeDamages.Count;

        if (_damageBuffer == null || computeShader == null || displayMaterial == null)
            return;

        if (count == 0)
            return;

        // Single GPU upload per frame, and only when the set changed (no per-hit ToArray()).
        // INVARIANT: buffer slots [count, _maxNumbers) are never cleared, so the draw is only
        // correct because we render exactly `count` instances AND every mutation of _activeDamages
        // (Add, cap-evict, prune) sets _bufferDirty so the live slice is re-uploaded. Keep it so.
        if (_bufferDirty)
        {
            _activeDamages.CopyTo(_uploadScratch);
            _damageBuffer.SetData(_uploadScratch, 0, 0, count);
            _bufferDirty = false;
        }

        int kernel = computeShader.FindKernel("UpdateNumbers");
        computeShader.SetBuffer(kernel, "_DamageBuffer", _damageBuffer);
        computeShader.SetFloat("_Time", currentTime);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(count / 64f), 1, 1);

        displayMaterial.SetBuffer("_DamageBuffer", _damageBuffer);
        displayMaterial.SetFloat("_CurrentTime", currentTime);

        Graphics.DrawMeshInstancedProcedural(
            quadMesh,
            0,
            displayMaterial,
            new Bounds(Vector3.zero, Vector3.one * 1000),
            count
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