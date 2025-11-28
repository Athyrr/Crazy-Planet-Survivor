using UnityEngine;

[CreateAssetMenu(fileName = "NewSpellData", menuName = "Survivor/Spell/Spell Data")]
public class SpellDataSO : ScriptableObject
{
    public string DisplayName;
    public GameObject SpellPrefab;

    public ESpellID ID;
    public float BaseCooldown = 5f;
    public float BaseDamage = 10f;
    public float BaseArea = 1f;
    public float BaseRange = 5f;
    public float BaseSpeed = 5f;
    public ESpellElement Element;
    public float Lifetime = 10f;

    // Ricochet settings
    public int Bounces;
    public float BouncesSearchRadius;

    public bool InstantiateOnce = false;
}