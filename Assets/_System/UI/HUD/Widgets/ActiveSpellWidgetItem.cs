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

    public void Refresh(SpellDataSO data, int databaseIndex, int level)
    {
        if (data == null)
            return;

        if (Icon)
            Icon.sprite = data.Icon;
    }
    
    public void RefreshCooldown(float current, float final)
    {
        bool onCooldown = current > 0f && final > 0f;
        float ratio = onCooldown ? Mathf.Clamp01(current / final) : 0f;

        if (CooldownOverlay)
        {
            if (CooldownOverlay.gameObject.activeSelf != onCooldown)
                CooldownOverlay.gameObject.SetActive(onCooldown);

            CooldownOverlay.fillAmount = 1 - ratio;
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