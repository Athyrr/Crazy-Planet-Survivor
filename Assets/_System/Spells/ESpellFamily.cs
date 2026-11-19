/// <summary>
/// Editing category of a spell (<see cref="SpellSO.Family"/>), set by hand: decides which SpellSO fields the
/// inspector shows and which family/prefab warnings apply. Projectile, Area and Summon are delivery families
/// (SPELL_TAXONOMY §2); EmitterOwner (Beam, FireOrbs) is an editing category only: a spell that carries emitters.
/// Not baked: SpellBlob does not read it.
/// </summary>
public enum ESpellFamily : byte
{
    Projectile = 0,
    Area = 1,
    Summon = 2,
    EmitterOwner = 3,
}
