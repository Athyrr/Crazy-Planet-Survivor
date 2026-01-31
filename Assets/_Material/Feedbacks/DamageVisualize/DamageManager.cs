using System.Collections;
using UnityEngine;
using System.Collections.Generic;
using EasyButtons;
using PrimeTween;
using Random = UnityEngine.Random;

[ExecuteAlways]
public class DamageManager : MonoBehaviour
{
    struct DamageData {
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

    private void InitBuffer() {
        if (_damageBuffer == null) {
            _damageBuffer = new ComputeBuffer(_maxNumbers, sizeof(float) * 6);
            // On initialise avec des données vides pour éviter des glitchs visuels
            _damageBuffer.SetData(new DamageData[_maxNumbers]);
        }
    }

    public void AddDamage(int val, Vector3 pos) {
        InitBuffer();

        if (_activeDamages.Count >= _maxNumbers) _activeDamages.RemoveAt(0);
        
        _activeDamages.Add(new DamageData {
            Position = pos,
            Value = (float)val,
            StartTime = GetCurrentTime(),
            DigitCount = val.ToString().Length
        }); 
        
        _damageBuffer.SetData(_activeDamages.ToArray());
        Debug.Log($"hyv; damage feedback applied {val}");
    }

    private float GetCurrentTime() {
        return Application.isPlaying ? Time.time : (float)Time.realtimeSinceStartup;
    }
    
    public void Start()
    {
        var coroutine = StartCoroutine(TrySpawnDamageVisualizer());
    }
    
    IEnumerator TrySpawnDamageVisualizer()
    {
        while (true)
        {
            AddDamage(Random.Range(10, 999), Random.insideUnitSphere * 1f);
            yield return new WaitForSeconds(1f);
        }
    }

    void Update() {
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

    private void OnDestroy() {
        ReleaseBuffer();
    }

    private void ReleaseBuffer() {
        if (_damageBuffer != null) {
            _damageBuffer.Release();
            _damageBuffer = null;
        }
    }
    
    [Button]
    void TestDamage() {
        AddDamage(Random.Range(10, 999999), Vector3.zero);
        
        // Sequence.Create()
        // Invoke((AddDamage(Random.Range(10, 999999), Vector3.zero), 1f);
    }
}