using System.Collections.Generic;
using UnityEngine;

/// <summary>Project view of a channeled beam (spec channeled-beam), replacing PilotoStudio.BeamEmitter on the VFX copy.
/// Read-only view: BeamVisualPresentationSystem pushes the endpoints and the width every frame; no gameplay reads it.
/// Show / Hide form and dissipate the beam (line width 0 ↔ full over the formation time); particles play / stop emitting.</summary>
[DisallowMultipleComponent]
public class BeamVisual : MonoBehaviour
{
    [Tooltip("Line renderers stretched between the two endpoints (world space).")]
    [SerializeField] private List<LineRenderer> _lines = new List<LineRenderer>();
    [Tooltip("SingleSidedEdge particle systems stretched along the beam.")]
    [SerializeField] private List<ParticleSystem> _edgeSystems = new List<ParticleSystem>();
    [Tooltip("Impact FX placed at the end point, facing the origin. Optional.")]
    [SerializeField] private GameObject _hitFx;
    [Tooltip("Seconds to form (Show) or dissipate (Hide) the beam.")]
    [SerializeField, Min(0f)] private float _formationTime = 0.15f;

    private float[] _baseWidths;
    private float[] _baseRates;
    private ParticleSystem[] _allSystems;
    private ParticleSystem[] _clearOnHide;
    private float _widthScale = 1f;
    private float _formation; // 0 = invisible, 1 = fully formed
    private bool _shown;

    private void Awake()
    {
        _allSystems = GetComponentsInChildren<ParticleSystem>(true);
        _baseWidths = new float[_lines.Count];
        for (int i = 0; i < _lines.Count; i++)
        {
            _baseWidths[i] = _lines[i].widthMultiplier;
            _lines[i].useWorldSpace = true;
            _lines[i].positionCount = 2;
        }
        _baseRates = new float[_edgeSystems.Count];
        for (int i = 0; i < _edgeSystems.Count; i++)
            _baseRates[i] = _edgeSystems[i].emission.rateOverTime.constant;
        // Infinite-lifetime systems never die once emission stops: they are cleared on Hide (finite trails fade out on their own).
        var infinite = new List<ParticleSystem>();
        foreach (var ps in _allSystems)
        {
            var lifetime = ps.main.startLifetime;
            if (lifetime.mode == ParticleSystemCurveMode.Constant && float.IsInfinity(lifetime.constantMax)) infinite.Add(ps);
        }
        _clearOnHide = infinite.ToArray();
        ApplyWidth();
    }

    // Play On Awake is on (project rule for ECS-spawned VFX); the bridge decides when the beam shows. Start runs after
    // every Awake/OnEnable, so it wins over Play On Awake unless Show() was already called this frame.
    private void Start()
    {
        if (_shown) return;
        foreach (var ps in _allSystems) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (_hitFx != null) _hitFx.SetActive(false);
    }

    public void Show()
    {
        if (_shown) return;
        _shown = true;
        foreach (var ps in _allSystems) ps.Play(false);
        if (_hitFx != null) _hitFx.SetActive(true);
    }

    public void Hide()
    {
        if (!_shown) return;
        _shown = false;
        foreach (var ps in _allSystems) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        foreach (var ps in _clearOnHide) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
    }

    /// <summary>k multiplies the authored widths (1 = the prefab as authored).</summary>
    public void SetWidth(float k) => _widthScale = Mathf.Max(0f, k);

    public void SetEndpoints(Vector3 a, Vector3 b)
    {
        Vector3 ab = b - a;
        if (ab.sqrMagnitude > 1e-6f) transform.SetPositionAndRotation(a, Quaternion.LookRotation(ab));
        else transform.position = a;

        foreach (var line in _lines)
        {
            line.SetPosition(0, b); // Piloto order: 0 = target, 1 = origin
            line.SetPosition(1, a);
        }

        float length = ab.magnitude;
        for (int i = 0; i < _edgeSystems.Count; i++)
        {
            var ps = _edgeSystems[i];
            Vector3 toB = b - ps.transform.position;
            if (toB.sqrMagnitude > 1e-6f) ps.transform.rotation = Quaternion.LookRotation(toB);
            float half = toB.magnitude * 0.5f; // the edge grows on both sides of its centre
            var shape = ps.shape;
            shape.rotation = new Vector3(0f, 90f, 0f);
            shape.radius = half;
            shape.position = new Vector3(0f, 0f, half);
            var emission = ps.emission;
            emission.rateOverTime = _baseRates[i] * (1f + Mathf.Max(0f, length - 5f) / 5f); // Piloto density rule
        }

        if (_hitFx != null)
        {
            _hitFx.transform.position = b;
            _hitFx.transform.LookAt(a);
        }
    }

    private void Update()
    {
        float target = _shown ? 1f : 0f;
        _formation = _formationTime > 0f ? Mathf.MoveTowards(_formation, target, Time.deltaTime / _formationTime) : target;
        ApplyWidth();
        if (!_shown && _formation <= 0f && _hitFx != null && _hitFx.activeSelf) _hitFx.SetActive(false);
    }

    private void ApplyWidth()
    {
        for (int i = 0; i < _lines.Count; i++)
            _lines[i].widthMultiplier = _baseWidths[i] * _widthScale * _formation;
    }
}
