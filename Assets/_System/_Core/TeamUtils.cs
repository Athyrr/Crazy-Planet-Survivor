using Unity.Entities;

/// <summary>The ONLY place that translates a team into collision layers (spec allegeance-summon §1). Burst-compatible.
/// Resolve falls back to "has Player → Player, else Enemy", so an entity not rebaked yet behaves exactly like eee06e4e.</summary>
public static class TeamUtils
{
    /// <summary>Layers of the entities this team fights: Player → Enemy, Enemy → Player, Neutral → none.</summary>
    public static uint HostileLayers(ETeam team)
    {
        switch (team)
        {
            case ETeam.Player: return CollisionLayers.Enemy;
            case ETeam.Enemy: return CollisionLayers.Player;
            default: return 0u;
        }
    }

    /// <summary>What a spell instance of this team may hit: its hostiles plus obstacles.</summary>
    public static uint TargetLayers(ETeam team) => HostileLayers(team) | CollisionLayers.Obstacle;

    public static bool IsHostile(ETeam a, ETeam b) => a != ETeam.Neutral && b != ETeam.Neutral && a != b;

    /// <summary>Pure resolution: Team when present, else Player when the entity has Player, else Enemy.</summary>
    public static ETeam Resolve(bool hasTeam, ETeam team, bool hasPlayer)
        => hasTeam ? team : (hasPlayer ? ETeam.Player : ETeam.Enemy);

    public static ETeam Resolve(Entity entity, in ComponentLookup<Team> teamLookup, in ComponentLookup<Player> playerLookup)
    {
        bool hasTeam = teamLookup.TryGetComponent(entity, out var team);
        return Resolve(hasTeam, team.Value, playerLookup.HasComponent(entity));
    }

    /// <summary>"Caster on the player's side" = the Player, or a Summon whose team is Player.</summary>
    public static bool IsPlayerSide(Entity entity, in ComponentLookup<Team> teamLookup, in ComponentLookup<Player> playerLookup)
        => Resolve(entity, in teamLookup, in playerLookup) == ETeam.Player;

    /// <summary>Hostile layers recovered from a spell instance's own TargetLayers (valid even when its caster is dead):
    /// keeps the Player/Enemy bits; 0 keeps the legacy Enemy search.</summary>
    public static uint HostileFromTargetLayers(uint targetLayers)
    {
        uint hostile = targetLayers & (CollisionLayers.Player | CollisionLayers.Enemy);
        return hostile != 0u ? hostile : CollisionLayers.Enemy;
    }
}
