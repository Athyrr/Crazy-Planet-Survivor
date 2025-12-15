using TMPro;
using UnityEngine;

/// <summary>
/// Represents le selected character prview container
/// </summary>
public class CharacterDetailView : MonoBehaviour
{
    [Header("UI Elements")]

    public Transform CharacterPreviewContainer;
    public TMP_Text CharacterNameText;
    public TMP_Text CharacterDescriptionText;

    public void Refresh(CharacterDataSO data)
    {
        if (data == null)
            return;

        if (data.UIPrefab != null && CharacterPreviewContainer != null)
        {
            foreach (Transform child in CharacterPreviewContainer.transform)
                GameObject.Destroy(child.gameObject);

          var characterObject = GameObject.Instantiate(data.UIPrefab, CharacterPreviewContainer.transform);
        }

        if (CharacterNameText != null)
            CharacterNameText.text = data.DisplayName;

        if (CharacterDescriptionText != null)
            CharacterDescriptionText.text = data.Description;
    }
}
