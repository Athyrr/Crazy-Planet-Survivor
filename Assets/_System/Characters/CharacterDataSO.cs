using UnityEngine;

[CreateAssetMenu(fileName = "NewCharacterData", menuName = "Survivor/Characters/Character Data")]
public class CharacterDataSO : ScriptableObject
{
    [Header("General")]

    [Tooltip("Name displayed in the UI.")]
    public string DisplayName = string.Empty;

    [TextArea(2, 4)]
    [Tooltip("Description displayed in the UI.")]
    public string Description;


    [Header("UI")]

    [Tooltip("Icon displayed in the UI.")]
    public Sprite Icon = null;

    [SerializeField]
    [Tooltip("The GameObject prefab that will be used for UI. Only renderer")]
    public GameObject UIPrefab;


    [Header("Model")]

    [SerializeField]
    [Tooltip("The GameObject prefab that will be baked into an Entity. Must have Authoring components.")]
    public GameObject GamePrefab;


    [Header("Upgrades")]

    [Tooltip("Character stats upgrades pool.")]
    public UpgradesDatabaseSO StatsUpgradesPool;

    [Tooltip("Character spell upgrades pool. Includes unlocks and upgrades.")]
    public UpgradesDatabaseSO SpellUpgradesPool;


    [Header("Spells")]

    [Tooltip("Character initial spells")]
    public SpellDataSO[] InitialSpells;

    [Header("Statistics")]

    [Tooltip("Character base stats")]
    public BaseStats BaseStats;
}
