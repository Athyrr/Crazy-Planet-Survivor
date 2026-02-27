using UnityEngine;

[CreateAssetMenu(menuName = "Survivor/Amulets/Amulet", fileName = "AmuletSO")]
public class AmuletSO : ScriptableObject
{
    [Header("UI Info")]
    public string DisplayName;  
    [TextArea(2,4)] 
    public string Description;  
    public GameObject UIPrefab;
    public Sprite Icon; 
    public int Price;  

    [Header("Effects")]
    public AmuletModifier[] Modifiers;

}

[System.Serializable]
public struct AmuletModifier
{
    public ECharacterStat Stat; 
    public EStatModiferStrategy Strategy; 
    public float Value;
}