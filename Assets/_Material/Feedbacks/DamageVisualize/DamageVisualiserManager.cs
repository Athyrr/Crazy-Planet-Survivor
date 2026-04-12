using UnityEngine;
using System.Collections.Generic;
using EasyButtons;
using PrimeTween;
using Random = UnityEngine.Random;
using Unity.Entities;
using UnityEditor;

[ExecuteAlways]
public class DamageFeedbackManager : MonoBehaviour
{
    public Color BaseDamageColor = Color.white;
    public Color CriticalDamageColor = Color.goldenRod;
    public Color BurnDamageColor = Color.darkRed;

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

    struct DamageData
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
    private List<DamageData> _activeDamages = new List<DamageData>();
    private const int _maxNumbers = 1000;

    private EntityManager _entityManager;
    private EntityQuery _damageFeedbackQuery;

    private void Start()
    {
        _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
        _damageFeedbackQuery = _entityManager.CreateEntityQuery(typeof(DamageFeedbackRequest));

        InitBuffer();
    }


    private void InitBuffer()
    {
        if (_damageBuffer == null)
        {
            // _damageBuffer = new ComputeBuffer(_maxNumbers, sizeof(float) * 6);
            _damageBuffer = new ComputeBuffer(_maxNumbers, sizeof(float) * 10);
            _damageBuffer.SetData(new DamageData[_maxNumbers]);
        }
    }

    public void AddDamage(int val, Vector3 pos, Color color)
    {
        InitBuffer();

        if (_activeDamages.Count >= _maxNumbers) _activeDamages.RemoveAt(0);

        _activeDamages.Add(new DamageData
        {
            Position = pos,
            Value = (float)val,
            StartTime = GetCurrentTime(),
            DigitCount = val.ToString().Length,
            Color = color
        });

        _damageBuffer.SetData(_activeDamages.ToArray());
    }

    private float GetCurrentTime()
    {
        return Application.isPlaying ? Time.time : (float)Time.realtimeSinceStartup;
    }

    private void Update()
    {
        if (World.DefaultGameObjectInjectionWorld != null)
        {
            if (!EditorApplication.isPlaying)
            {
                _entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
                _damageFeedbackQuery = _entityManager.CreateEntityQuery(typeof(DamageFeedbackRequest));
            }

            if (!_damageFeedbackQuery.IsEmpty)
            {
                var requests =
                    _damageFeedbackQuery.ToComponentDataArray<DamageFeedbackRequest>(Unity.Collections.Allocator.Temp);
                var entities = _damageFeedbackQuery.ToEntityArray(Unity.Collections.Allocator.Temp);

                for (int i = 0; i < entities.Length; i++)
                {
                    var req = requests[i];

                    var color = BaseDamageColor;

                    if (req.IsCritical)
                        color = CriticalDamageColor;

                    else if (req.IsBurn)
                        color = BurnDamageColor;

                    AddDamage(req.Amount,
                        (Vector3)req.Transform.Position + (Vector3)req.Transform.Up() * (1.5f * Random.Range(1, 3f)),
                        color);

                    _entityManager.DestroyEntity(entities[i]);
                }

                requests.Dispose();
                entities.Dispose();
            }
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

        Graphics.DrawMeshInstancedProcedural(quadMesh, 0, displayMaterial,
            new Bounds(Vector3.zero, Vector3.one * 1000), _activeDamages.Count);
    }

    private void OnDestroy()
    {
        ReleaseBuffer();
        _damageFeedbackQuery.Dispose();
        _damageBuffer?.Release();
    }

    private void ReleaseBuffer()
    {
        if (_damageBuffer != null)
        {
            _damageBuffer.Release();
            _damageBuffer = null;
        }
    }

    [Button]
    void TestDamage()
    {
        var color = Color.white;

        Sequence.Create(cycles: 10, Sequence.SequenceCycleMode.Yoyo)
            .ChainCallback(() => AddDamage(Random.Range(10, 999999), transform.position, color))
            .ChainDelay(0.1f)
            .ChainCallback(() => AddDamage(Random.Range(10, 9999), transform.position, color))
            .ChainDelay(0.1f)
            .ChainCallback(() => AddDamage(Random.Range(10, 99), transform.position, color));
    }

    [Button]
    void TestDamageSingle()
    {
        AddDamage(Random.Range(10, 999999), transform.position, Color.white);
    }
}