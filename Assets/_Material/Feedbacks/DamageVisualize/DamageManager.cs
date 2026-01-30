using System;
using System.Collections;
using UnityEngine;
using System.Collections.Generic;
using Random = UnityEngine.Random;

#if UNITY_EDITOR
using UnityEditor;
#endif

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

    [SerializeField] [Tooltip("vraiment pas fonctionnel merci de ne jamis l'utiliser")]
    private bool _activeEditorDebug = false;
    
    private ComputeBuffer _damageBuffer;
    private List<DamageData> _activeDamages = new List<DamageData>();
    private const int _maxNumbers = 1000;

    // Utilisation d'un flag pour éviter les fuites de mémoire au changement de script
    private void OnEnable() {
        InitBuffer();
        #if UNITY_EDITOR
        // Force l'update même si la scène ne bouge pas
        if (_activeEditorDebug)
            EditorApplication.update += EditorUpdate;
        #endif
    }

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
        Debug.Log("hyv; damage feedback applied");
    }

    private float GetCurrentTime() {
        return Application.isPlaying ? Time.time : (float)Time.realtimeSinceStartup;
    }

    #if UNITY_EDITOR
    void EditorUpdate() {
        if (!Application.isPlaying) {
            // Demande à Unity de redessiner l'interface et de déclencher Update() (askip)
            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }
    }
    #endif
    
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

        // 1. Mise à jour via Compute Shader
        int kernel = computeShader.FindKernel("UpdateNumbers");
        computeShader.SetBuffer(kernel, "_DamageBuffer", _damageBuffer);
        computeShader.SetFloat("_Time", currentTime);
        computeShader.Dispatch(kernel, Mathf.CeilToInt(_maxNumbers / 64f), 1, 1);

        // 2. Configuration du matériel
        displayMaterial.SetBuffer("_DamageBuffer", _damageBuffer);
        displayMaterial.SetFloat("_CurrentTime", currentTime);
        
        // 3. Rendu (Fonctionne en Editor et en Game)
        Graphics.DrawMeshInstancedProcedural(quadMesh, 0, displayMaterial, 
            new Bounds(Vector3.zero, Vector3.one * 1000), _activeDamages.Count);
    }

    private void OnDisable() {
        ReleaseBuffer();
        #if UNITY_EDITOR
        EditorApplication.update -= EditorUpdate;
        #endif
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
    
    // [Butto]
    void TestDamage() {
        AddDamage(Random.Range(10, 999), Vector3.zero);
    }
}