using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacterData", menuName = "Survivor/Characters/Character Data")]
public class CharacterSO : EntityDefinitionSO
{
    [Header("UI")]
    [SerializeField] [Tooltip("The GameObject prefab that will be used for UI. Only renderer")]
    public GameObject UIPrefab;
    
    [Header("Model")]
    [SerializeField]
    [Tooltip("The GameObject prefab that will be baked into an Entity. Must have Authoring components.")]
    public GameObject GamePrefab;
    
    [Header("Upgrades")] [Tooltip("Character stats upgrades pool.")]
    public UpgradesDatabaseSO StatsUpgradesPool;

    [Tooltip("Character spell upgrades pool. Includes unlocks and upgrades.")]
    public UpgradesDatabaseSO SpellUpgradesPool;


    [Header("Spells")] [Tooltip("Character initial spells")]
    public SpellSO[] InitialSpells;

    [Header("Cost")] [Tooltip("Resources required to purchase this character.")]
    public ResourceCost[] PurchaseCost;

    [Header("Statistics")] [Tooltip("Character base stats")]
    public CoreStats coreStats;

    [Header("Settings Domains")]
    [Tooltip("This character's Dash tuning. Discovered automatically by the Settings Browser window.")]
    public DashSO Dash;
}