using PrimeTween;
using TMPro;
using UnityEngine;

/// <summary>
/// Presentational "boss name card" for the run HUD: reveals a message with a Zelda / Hollow-Knight style
/// fade — fade in with a slight scale-up, hold, fade out — then deactivates itself.
///
/// It does NOT decide when to appear: the <see cref="RunManager"/> owns that (it detects the boss and
/// activates this panel, which is disabled by default). Drive it by calling <see cref="Show"/>.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class BossAppearMessageUI : MonoBehaviour
{
    [Header("Refs")]
    [Tooltip("Label shown. Defaults to the TMP text on this object.")]
    [SerializeField] private TMP_Text _text;

    [SerializeField] private CanvasGroup _group;

    [Header("Timing")]
    [SerializeField] private float _fadeInDuration = 0.7f;
    [SerializeField] private float _holdDuration = 1.6f;
    [SerializeField] private float _fadeOutDuration = 1.0f;

    [Tooltip("Scale the text grows from while fading in (1 = no scale).")]
    [SerializeField] private float _startScale = 0.85f;

    [SerializeField] private Ease _fadeInEase = Ease.OutCubic;
    [SerializeField] private Ease _fadeOutEase = Ease.InCubic;

    private Sequence _sequence;

    private void Awake() => CacheRefs();

    private void OnDisable()
    {
        if (_sequence.isAlive)
            _sequence.Stop();
    }

    /// <summary>
    /// Plays the reveal for <paramref name="message"/> (fade in + scale, hold, fade out) and deactivates
    /// this GameObject once it finishes. The caller is expected to have just activated the object.
    /// </summary>
    public void Show(string message)
    {
        CacheRefs();
        if (_text == null || _group == null)
            return;

        if (_sequence.isAlive)
            _sequence.Stop();

        _text.text = message;
        _group.alpha = 0f;
        transform.localScale = Vector3.one * _startScale;

        _sequence = Sequence.Create()
            .Group(Tween.Alpha(_group, 0f, 1f, _fadeInDuration, _fadeInEase))
            .Group(Tween.Scale(transform, _startScale, 1f, _fadeInDuration, _fadeInEase))
            .ChainDelay(_holdDuration)
            .Chain(Tween.Alpha(_group, 1f, 0f, _fadeOutDuration, _fadeOutEase))
            .ChainCallback(() => gameObject.SetActive(false));
    }

    private void CacheRefs()
    {
        if (_text == null)
            _text = GetComponent<TMP_Text>();
        if (_group == null)
            _group = GetComponent<CanvasGroup>();
    }
}
