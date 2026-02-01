using UnityEngine;
using System.Collections.Generic;
using EasyButtons;
using PrimeTween;
using Random = UnityEngine.Random;

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

        DontDestroyOnLoad(gameObject);
    }

    #endregion

    struct DamageData
    {
        public Vector3 Position;
        public float Value;
        public float StartTime;
        public int DigitCount;
    }

    [SerializeField] public ComputeShader computeShader;
    [SerializeField] public Material displayMaterial;
    [SerializeField] public Mesh quadMesh;

    private ComputeBuffer _damageBuffer;
    private List<DamageData> _activeDamages = new List<DamageData>();
    private const int _maxNumbers = 1000;

    private void InitBuffer()
    {
        if (_damageBuffer == null)
        {
            _damageBuffer = new ComputeBuffer(_maxNumbers, sizeof(float) * 6);
            // On initialise avec des données vides pour éviter des glitchs visuels
            _damageBuffer.SetData(new DamageData[_maxNumbers]);
        }
    }

    public void AddDamage(int val, Vector3 pos)
    {
        InitBuffer();

        if (_activeDamages.Count >= _maxNumbers) _activeDamages.RemoveAt(0);

        _activeDamages.Add(new DamageData
        {
            Position = pos,
            Value = (float)val,
            StartTime = GetCurrentTime(),
            DigitCount = val.ToString().Length
        });

        _damageBuffer.SetData(_activeDamages.ToArray());
        Debug.Log($"hyv; damage feedback applied {val}");
    }

    private float GetCurrentTime()
    {
        return Application.isPlaying ? Time.time : (float)Time.realtimeSinceStartup;
    }

    void FixedUpdate()
    {
        if (_activeDamages.Count == 0 || _damageBuffer == null || computeShader == null || displayMaterial == null)
            return;

        float currentTime = GetCurrentTime();

        int kernel = computeShader.FindKernel("UpdateNumbers");
        computeShader.SetBuffer(kernel, "_DamageBuffer", _damageBuffer);
        computeShader.SetFloat("_Time", currentTime);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(_maxNumbers / 64f), 1, 1);

        displayMaterial.SetBuffer("_DamageBuffer", _damageBuffer);
        displayMaterial.SetFloat("_CurrentTime", currentTime);

        // render part
        Graphics.DrawMeshInstancedProcedural(quadMesh, 0, displayMaterial,
            new Bounds(Vector3.zero, Vector3.one * 1000), _activeDamages.Count);
    }

    private void OnDestroy()
    {
        ReleaseBuffer();
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
        Sequence.Create(cycles: 10, Sequence.SequenceCycleMode.Yoyo)
            .ChainCallback(() => AddDamage(Random.Range(10, 999999), transform.position))
            .ChainDelay(0.1f)
            .ChainCallback(() => AddDamage(Random.Range(10, 9999), transform.position))
            .ChainDelay(0.1f)
            .ChainCallback(() => AddDamage(Random.Range(10, 99), transform.position));
    }
}