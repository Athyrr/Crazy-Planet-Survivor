using Unity.Entities;

/// <summary>Allegiance of a combatant (SPELL_TAXONOMY §8). Neutral is modelled, unused in game.</summary>
public enum ETeam : byte { Player, Enemy, Neutral }

/// <summary>Team of an entity. Baked by PlayerAuthoring (Player) and EnemyAuthoring (Enemy); set on a Summon at spawn
/// (its owner's team). Read only through <see cref="TeamUtils"/> — never translate a team into layers anywhere else.</summary>
public struct Team : IComponentData
{
    public ETeam Value;
}
