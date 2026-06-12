using _System.Settings;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared styling for text-only menu buttons / rows: the button shows no background image; its
/// TMP label is the color-tint target instead, so the text turns blue on hover / focus / press and
/// greys out when the button is disabled. All colors come from <see cref="CpUISettings"/> so the
/// main menu and the settings panel stay in sync.
/// </summary>
public static class UILabelPalette
{
    /// <summary>The label ColorBlock built from the shared palette (idle / highlight / disabled).</summary>
    public static ColorBlock LabelColorBlock()
    {
        Color highlight = CpUISettings.LabelHighlightColor;

        return new ColorBlock
        {
            normalColor = CpUISettings.LabelColor,
            highlightedColor = highlight,
            pressedColor = highlight,
            selectedColor = highlight,
            disabledColor = CpUISettings.LabelColorDisabled,
            colorMultiplier = 1f,
            fadeDuration = 0.1f,
        };
    }

    /// <summary>
    /// Points the button's color tint at its TMP label so state changes recolor the text rather than
    /// a background image. Disabled buttons grey out automatically.
    /// </summary>
    public static void ApplyToButton(Button button) => ApplyToButton(button, null);

    /// <summary>
    /// Same as <see cref="ApplyToButton(Button)"/> but tints a specific graphic (e.g. a row's "value"
    /// text rather than its name). When <paramref name="tintTarget"/> is null it falls back to the
    /// button's own label. Every TMP child is kept a raycast target so the whole row stays hoverable /
    /// clickable without a background Image.
    /// </summary>
    public static void ApplyToButton(Button button, Graphic tintTarget)
    {
        if (button == null)
            return;

        if (tintTarget == null)
            tintTarget = button.GetComponentInChildren<TMP_Text>(true);
        if (tintTarget == null)
            return;

        var texts = button.GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
            texts[i].raycastTarget = true;

        // ColorTint multiplies the target's vertex color, so keep its mesh white and let the tint
        // supply the actual color.
        tintTarget.color = Color.white;

        button.transition = Selectable.Transition.ColorTint;
        button.targetGraphic = tintTarget;
        button.colors = LabelColorBlock();

        // A Selectable only repaints on a state change, so paint the current (idle / disabled) tint
        // now; hover / focus / press transitions take over from here.
        Color initial = button.interactable ? CpUISettings.LabelColor : CpUISettings.LabelColorDisabled;
        tintTarget.canvasRenderer.SetColor(initial);
    }
}
