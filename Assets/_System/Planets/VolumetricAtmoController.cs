using UnityEngine;

[ExecuteAlways]
[RequireComponent(typeof(Renderer))]
public class VolumetricAtmoController : MonoBehaviour
{
    private static readonly int ID_PlanetCenter    = Shader.PropertyToID("_PlanetCenter");
    private static readonly int ID_PlanetRadius    = Shader.PropertyToID("_PlanetRadius");
    private static readonly int ID_AtmoRadius      = Shader.PropertyToID("_AtmoRadius");
    private static readonly int ID_AtmoColor       = Shader.PropertyToID("_AtmoColor");
    private static readonly int ID_AtmoTint        = Shader.PropertyToID("_AtmoTint");
    private static readonly int ID_Density         = Shader.PropertyToID("_Density");
    private static readonly int ID_Falloff         = Shader.PropertyToID("_Falloff");
    private static readonly int ID_Intensity       = Shader.PropertyToID("_Intensity");
    private static readonly int ID_SunDirection    = Shader.PropertyToID("_SunDirection");
    private static readonly int ID_SunColor        = Shader.PropertyToID("_SunColor");
    private static readonly int ID_ScatteringPower = Shader.PropertyToID("_ScatteringPower");

    [Header("References")]
    [Tooltip("Transform de la planète (centre utilisé par le shader). Par défaut : parent.")]
    public Transform PlanetTransform;
    [Tooltip("Soleil directionnel. Si vide, cherche RenderSettings.sun ou la première Directional Light.")]
    public Light Sun;

    [Header("Geometry")]
    [Min(0f)] public float PlanetRadius = 1f;
    [Tooltip("Ratio du rayon de l'atmosphère par rapport au rayon planète.")]
    [Min(1f)] public float AtmoRadiusMultiplier = 1.15f;
    [Tooltip("Applique AtmoRadiusMultiplier sur localScale pour englober la planète.")]
    public bool AutoScaleToAtmoRadius = true;

    [Header("Appearance")]
    [ColorUsage(true, true)] public Color AtmoColor = new Color(0.35f, 0.6f, 1f, 1f);
    [ColorUsage(true, true)] public Color AtmoTint  = Color.white;
    [Min(0f)] public float Density         = 1f;
    [Min(0f)] public float Falloff         = 2f;
    [Min(0f)] public float Intensity       = 1f;
    [Min(0f)] public float ScatteringPower = 4f;

    [Header("Sun Override")]
    public bool OverrideSunColor = false;
    [ColorUsage(true, true)] public Color SunColorOverride = Color.white;

    private Renderer _renderer;
    private MaterialPropertyBlock _mpb;

    private void OnEnable()
    {
        _renderer = GetComponent<Renderer>();
        _mpb ??= new MaterialPropertyBlock();

        if (PlanetTransform == null)
            PlanetTransform = transform.parent != null ? transform.parent : transform;

        ResolveSun();
        Apply();
    }

    private void LateUpdate()
    {
        Apply();
    }

    private void ResolveSun()
    {
        if (Sun != null) return;

        if (RenderSettings.sun != null)
        {
            Sun = RenderSettings.sun;
            return;
        }

        foreach (var light in FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (light.type == LightType.Directional)
            {
                Sun = light;
                return;
            }
        }
    }

    private void Apply()
    {
        if (_renderer == null) return;

        if (AutoScaleToAtmoRadius && PlanetTransform != null)
        {
            float atmoRadius = PlanetRadius * AtmoRadiusMultiplier;
            transform.localScale = Vector3.one * (atmoRadius * 2f);
        }

        Vector3 center = PlanetTransform != null ? PlanetTransform.position : transform.position;
        Vector3 sunDir = Sun != null ? -Sun.transform.forward : Vector3.up;
        Color sunColor = OverrideSunColor || Sun == null
            ? SunColorOverride
            : Sun.color * Sun.intensity;

        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetVector(ID_PlanetCenter,    center);
        _mpb.SetFloat (ID_PlanetRadius,    PlanetRadius);
        _mpb.SetFloat (ID_AtmoRadius,      PlanetRadius * AtmoRadiusMultiplier);
        _mpb.SetColor (ID_AtmoColor,       AtmoColor);
        _mpb.SetColor (ID_AtmoTint,        AtmoTint);
        _mpb.SetFloat (ID_Density,         Density);
        _mpb.SetFloat (ID_Falloff,         Falloff);
        _mpb.SetFloat (ID_Intensity,       Intensity);
        _mpb.SetVector(ID_SunDirection,    sunDir.normalized);
        _mpb.SetColor (ID_SunColor,        sunColor);
        _mpb.SetFloat (ID_ScatteringPower, ScatteringPower);
        _renderer.SetPropertyBlock(_mpb);
    }

    private void OnValidate()
    {
        if (!isActiveAndEnabled) return;
        _renderer ??= GetComponent<Renderer>();
        _mpb ??= new MaterialPropertyBlock();
        Apply();
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 center = PlanetTransform != null ? PlanetTransform.position : transform.position;
        Gizmos.color = new Color(0.3f, 0.6f, 1f, 0.4f);
        Gizmos.DrawWireSphere(center, PlanetRadius);
        Gizmos.color = new Color(AtmoColor.r, AtmoColor.g, AtmoColor.b, 0.8f);
        Gizmos.DrawWireSphere(center, PlanetRadius * AtmoRadiusMultiplier);
    }
}
