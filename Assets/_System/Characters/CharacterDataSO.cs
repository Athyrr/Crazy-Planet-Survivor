using UnityEngine;
using UnityEngine.UI;

[CreateAssetMenu(fileName = "NewCharacterData", menuName = "Survivor/Characters/Character Data")]
public class CharacterDataSO : ScriptableObject
{
    [Header("General")]

    [Tooltip("Name displayed in the UI.")]
    public string DisplayName = string.Empty;

    [TextArea(2, 4)]
    [Tooltip("Description displayed in the UI.")]
    public string Description;

    [Tooltip("Icon displayed in the UI.")]
    public Sprite Icon = null;

    [SerializeField]
    [Tooltip(tooltip: "The GameObject prefab that will be baked into an Entity. Must have Authoring components.")]
    public GameObject Prefab;


    [Header("Spells")]

    [Tooltip("Character initial spells")]
    public SpellDataSO[] InitialSpells;


    [Header("Statistics")]

    [Tooltip("Character base stats")]
    public BaseStats BaseStats;


    //[Header("Stats modfiers")]

    //[Tooltip("Character intial stats modifiers")]
    //public StatModifier[] StatModifier;
}
