using _System.Settings;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

/// <summary>
/// UI element for a spell unlocked and active during a run.
/// </summary>
public class ActiveSpellWidgetItem : UIViewItemBase
{
    public Image Icon;
    public Image Border;

    public Image CooldownOverlay;

    [Tooltip("Remaining cooldown text in seconds.")]
    public TMP_Text CooldownText;

    [Tooltip("Hide the cooldown text while the spell is ready (no active cooldown).")]
    public bool HideTextWhenReady = true;

    private bool _baseCaptured;
    private bool _highlighted;
    private Vector3 _baseScale = Vector3.one;
    private Color _baseBorderColor = Color.white;
    private Tween _scaleTween;
    private Tween _colorTween;

    public void Refresh(SpellSO data, int databaseIndex, int level)
    {
        if (data == null)
            return;

        if (Icon)
            Icon.sprite = data.Icon;
    }
    
    /// <summary>Hides cooldown visuals (used where the widget is a plain spell icon, e.g. the upgrade strip).</summary>
    public void HideCooldown()
    {
        if (CooldownOverlay)
            CooldownOverlay.gameObject.SetActive(false);
        if (CooldownText)
            CooldownText.gameObject.SetActive(false);
    }

    /// <summary>Tints the border to the upgrade highlight color and pops the scale; restores both when off.</summary>
    public void SetHighlighted(bool highlighted)
    {
        if (!_baseCaptured)
        {
            _baseCaptured = true;
            _baseScale = transform.localScale;
            if (Border)
                _baseBorderColor = Border.color;
        }

        if (_highlighted == highlighted)
            return;
        _highlighted = highlighted;

        if (_scaleTween.isAlive)
            _scaleTween.Stop();
        if (_colorTween.isAlive)
            _colorTween.Stop();

        float duration = CpUISettings.HoverDuration;
        Vector3 targetScale = highlighted ? _baseScale * CpUISettings.HoverScale : _baseScale;
        if (transform.localScale != targetScale)
            _scaleTween = Tween.Scale(transform, targetScale, duration, CpUISettings.HoverEase, useUnscaledTime: true);

        if (Border)
        {
            Color targetColor = highlighted ? CpUISettings.UpgradeCardHighlightColor : _baseBorderColor;
            if (Border.color != targetColor)
                _colorTween = Tween.Color(Border, targetColor, duration, CpUISettings.HoverEase, useUnscaledTime: true);
        }
    }

    private void OnDisable()
    {
        if (!_baseCaptured)
            return;
        if (_scaleTween.isAlive) _scaleTween.Stop();
        if (_colorTween.isAlive) _colorTween.Stop();
        transform.localScale = _baseScale;
        if (Border)
            Border.color = _baseBorderColor;
        _highlighted = false;
    }

    public void RefreshCooldown(float current, float final)
    {
        bool onCooldown = current > 0f && final > 0f;

        float remainingRatio = onCooldown ? Mathf.Clamp01(current / final) : 0f;

        if (CooldownOverlay)
        {
            if (CooldownOverlay.gameObject.activeSelf != onCooldown)
                CooldownOverlay.gameObject.SetActive(onCooldown);

            CooldownOverlay.fillAmount = remainingRatio;
        }

        if (CooldownText)
        {
            if (onCooldown)
            {
                if (!CooldownText.gameObject.activeSelf)
                    CooldownText.gameObject.SetActive(true);

                CooldownText.text = current >= 1f
                    ? Mathf.CeilToInt(current).ToString()
                    : current.ToString("0.0");
            }
            else if (HideTextWhenReady)
            {
                if (CooldownText.gameObject.activeSelf)
                    CooldownText.gameObject.SetActive(false);
            }
            else
            {
                CooldownText.text = string.Empty;
            }
        }
    }

    public void PlayUpgradeFeedback(int activeSpellLevel)
    {
        // todo feedback
    }

    public override void OnPointerEnter(PointerEventData eventData)
    {
        // todo Spell detail tooltip
    }

    public override void OnPointerClick(PointerEventData eventData)
    {
    }
}