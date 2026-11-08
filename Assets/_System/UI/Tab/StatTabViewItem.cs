using _System.Settings;
using PrimeTween;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

public class StatTabViewItem : UIViewItemBase
{
    public TMP_Text Label;
    public TMP_Text Value;

    private bool _colorsCaptured;
    private bool _highlighted;
    private Color _baseLabelColor;
    private Color _baseValueColor;
    private Tween _labelTween;
    private Tween _valueTween;

    public  void Refresh(string labelText, string valueText)
    {
        Label.text = labelText;
        Value.text = valueText;
    }

    /// <summary>Tints label + value to the upgrade highlight color (fade, unscaled time); restores when off.</summary>
    public void SetHighlighted(bool highlighted)
    {
        if (!_colorsCaptured)
        {
            _colorsCaptured = true;
            _baseLabelColor = Label.color;
            // Note: Value text carries rich-text <color> (StatsFormatUtils.Colorize), which overrides this tint.
            _baseValueColor = Value.color;
        }

        if (_highlighted == highlighted)
            return;
        _highlighted = highlighted;

        StartFade(ref _labelTween, Label, highlighted ? CpUISettings.UpgradeCardHighlightColor : _baseLabelColor);
        StartFade(ref _valueTween, Value, highlighted ? CpUISettings.UpgradeCardHighlightColor : _baseValueColor);
    }

    private static void StartFade(ref Tween tween, TMP_Text text, Color target)
    {
        if (tween.isAlive)
            tween.Stop();
        if (text.color == target)
            return;
        tween = Tween.Color(text, target, CpUISettings.HoverDuration, CpUISettings.HoverEase, useUnscaledTime: true);
    }

    private void OnDisable()
    {
        if (!_colorsCaptured)
            return;
        if (_labelTween.isAlive) _labelTween.Stop();
        if (_valueTween.isAlive) _valueTween.Stop();
        Label.color = _baseLabelColor;
        Value.color = _baseValueColor;
        _highlighted = false;
    }

    public override void OnPointerEnter(PointerEventData eventData)
    {
    }

    public override void OnPointerClick(PointerEventData eventData)
    {
    }
}
