using TMPro;
using Unity.Entities;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives a loading bar from the ECS <see cref="SceneLoadingState.Progress"/> value produced by
/// <c>SceneLoadingSystem</c>. Attach to any GameObject under the loading canvas and wire a filled
/// <see cref="Image"/> (and, optionally, a percentage label). The displayed value is smoothed so the
/// bar glides toward the real progress and snaps back to 0 when a new load starts.
/// </summary>
public class LoadingProgressUI : MonoBehaviour
{
    [Tooltip("Optional Slider (Min Value 0, Max Value 1). Its value is driven from 0 to 1.")]
    [SerializeField]
    private Slider _slider;

    [Tooltip("Optional Image with Image Type = Filled. Its fillAmount is driven from 0 to 1.")]
    [SerializeField]
    private Image _fillImage;

    [Tooltip("Optional label; shows the rounded percentage (e.g. \"73%\").")]
    [SerializeField]
    private TMP_Text _percentText;

    [Tooltip("How fast the bar catches up to the real progress (units per second). Higher = snappier.")]
    [SerializeField]
    private float _smoothSpeed = 3f;

    private EntityQuery _query;
    private bool _initialized;
    private float _displayed;

    private void OnEnable()
    {
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null)
            return;

        _query = world.EntityManager.CreateEntityQuery(typeof(SceneLoadingState));
        _initialized = true;
        _displayed = 0f;
        Apply(0f);
    }

    private void Update()
    {
        if (!_initialized || _query.IsEmpty)
            return;

        float target = Mathf.Clamp01(_query.GetSingleton<SceneLoadingState>().Progress);

        // A new load resets Progress to 0: snap down instantly rather than draining the bar.
        if (target < _displayed)
            _displayed = target;
        else
            _displayed = Mathf.MoveTowards(_displayed, target, _smoothSpeed * Time.unscaledDeltaTime);

        Apply(_displayed);
    }

    private void Apply(float value)
    {
        if (_slider != null)
            _slider.SetValueWithoutNotify(value);

        if (_fillImage != null)
            _fillImage.fillAmount = value;

        if (_percentText != null)
            _percentText.text = Mathf.RoundToInt(value * 100f) + "%";
    }
}
