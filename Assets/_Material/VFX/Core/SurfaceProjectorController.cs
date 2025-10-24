using UnityEngine;

[ExecuteInEditMode] 
public class SurfaceProjectorController : MonoBehaviour
{
    [Tooltip("Planet trasnform.")]
    public Transform PlanetTransform;

    [Tooltip("Planet radius.")]
    public float PlanetRadius = 50f;

    private Material _materialInstance;

    void Start()
    {
        var renderer = GetComponent<Renderer>();
        if (renderer == null)
        {
            Debug.LogError("Renderer not found !");
            return;
        }

        _materialInstance = renderer.material;

        UpdateShaderProperties();
    }

    private void OnValidate()
    {
        Start();
    }

    void Update()
    {
        if (_materialInstance != null && PlanetTransform != null)
        {
            UpdateShaderProperties();
        }
    }

    private void UpdateShaderProperties()
    {
        _materialInstance.SetVector("_PlanetCenter", PlanetTransform.position);
        _materialInstance.SetFloat("_PlanetRadius", PlanetRadius);
    }
}