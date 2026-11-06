using Unity.Entities;

/// <summary>Generalizes the dash-only i-frame infra (Invincible/DashIFrames) to "the player is briefly
/// immune after any resolved hit" — a target surrounded by N enemies no longer takes all N simultaneous
/// hits. Player-only; enemies never carry this. Window is a flat 0.3s (Brotato/Vampire-Survivors range);
/// %-HP-scaled duration is a documented future refinement, not required by the current design.</summary>
public struct GlobalIFrames : IComponentData, IEnableableComponent
{
    public float RemainingTime;

    /// <summary>Window granted after a resolved, damaging hit on the player (contact and area paths).</summary>
    public const float OnHitDuration = 0.3f;
}
