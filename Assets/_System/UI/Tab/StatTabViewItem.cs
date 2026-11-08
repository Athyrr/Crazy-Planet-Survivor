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
    private Tween _labelTween;

    public  void Refresh(string labelText, string valueText)
    {
        Label.text = labelText;
        Value.text = valueText;
    }

    /// <summary>Tints the label to the upgrade highlight color (fade, unscaled time); restores when off.
    /// The value keeps its sign color (rich-text tag from StatsFormatUtils) on purpose.</summary>
    public void SetHighlighted(bool highlighted)
    {
        if (!_colorsCaptured)
        {
            _colorsCaptured = true;
            _baseLabelColor = Label.color;
        }

        if (_highlighted == highlighted)
            return;
        _highlighted = highlighted;

        StartFade(ref _labelTween, Label, highlighted ? CpUISettings.UpgradeCardHighlightColor : _baseLabelColor);
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
        Label.color = _baseLabelColor;
        _highlighted = false;
    }

    public override void OnPointerEnter(PointerEventData eventData)
    {
    }

    public override void OnPointerClick(PointerEventData eventData)
    {
    }
}
