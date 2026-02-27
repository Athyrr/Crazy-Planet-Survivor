using TMPro;
using UnityEngine;

public class AmuletDetailView : MonoBehaviour
{
    [Header("Detail Container")] public Transform AmuletPreviewContainer;

    [Header("Default Amulet Details")] public GameObject DefaultAmulet;
    public string DefaultAmuletNameText;
    public string DefaultAmuletDescriptionText;

    [Header("Detail Texts")] public TMP_Text AmuletNameText;
    public TMP_Text AmuletDescriptionText;

    private AmuletShopUIController _controller;

    public void Refresh(AmuletSO data, bool isUnlocked)
    {
        Clear();

        if (data == null || data.UIPrefab == null || AmuletPreviewContainer == null)
            return;

        if (!isUnlocked)
        {
            ShowDefaultAmulet();
            return;
        }

        var amuletObject = GameObject.Instantiate(data.UIPrefab, AmuletPreviewContainer.transform);
        if (AmuletNameText != null)
            AmuletNameText.text = data.DisplayName;

        if (AmuletDescriptionText != null)
            AmuletDescriptionText.text = data.Description;
    }

    public void ShowDefaultAmulet()
    {
        Clear();

        if (DefaultAmulet != null && AmuletPreviewContainer != null)
            GameObject.Instantiate(DefaultAmulet, AmuletPreviewContainer.transform);

        if (AmuletNameText != null)
            AmuletNameText.text = DefaultAmuletNameText;

        if (AmuletDescriptionText != null)
            AmuletDescriptionText.text = DefaultAmuletDescriptionText;
    }

    public void Clear()
    {
        foreach (Transform child in AmuletPreviewContainer.transform)
            GameObject.Destroy(child.gameObject);
    }
}