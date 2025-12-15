using System;
using TMPro;
using Unity.VisualScripting;
using UnityEngine;

/// <summary>
/// Represents le selected character prview container
/// </summary>
public class CharacterDetailView : MonoBehaviour
{
    [Header("UI Elements")]

    //public GameObject CharacterPrefab;
    public TMP_Text CharacterNameText;
    public TMP_Text CharacterDescriptionText;

    public void Refresh(CharacterDataSO data)
    {
        if (data == null) 
            return;

    
        //@todo display character model

        if (CharacterNameText != null)
            CharacterNameText.text = data.DisplayName;

        if (CharacterDescriptionText != null)
            CharacterDescriptionText.text = data.Description;
    }
}
