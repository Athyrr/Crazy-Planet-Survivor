using Unity.Entities;
using UnityEngine;

[RequireComponent(typeof(DestructibleAuthoring))]
public class EnemyAuthoring : MonoBehaviour
{
    public Renderer MainRenderer;

    [Header("Movement precision")]
    [Tooltip(
        "If true, the enemy will be snapped perfectly on the ground following the terrain height. Otherwise, it will follow the base radius.")]
    public bool UseSnappedMovement = true;

    [Header("Movement feel")]
    [Tooltip("Acceleration in units/s². Leave at 0 to inherit CpBaseEnemySettings. Raise for darting " +
             "enemies, lower for heavy ones that take time to get going.")]
    public float Acceleration = 0f;

    [Tooltip("Max turn rate in degrees/second. Leave at 0 to inherit CpBaseEnemySettings. Lower it on " +
             "big enemies so they describe wide curves instead of pivoting on the spot.")]
    public float MaxTurnRateDeg = 0f;

    [Header("Stats")] public CoreStats BaseStats;

    [Header("Spells")] public SpellDataSO[] InitialSpells;

    [Header("Contact damage")]
    [Tooltip("Damage dealt to whatever Destructible this enemy's body touches (the player). ")]
    public float ContactDamage = 10f;

    private class Baker : Baker<EnemyAuthoring>
    {
        public override void Bake(EnemyAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            if (authoring.MainRenderer != null)
                AddComponentObject(entity, new VisualRendererLink { Renderer = authoring.MainRenderer });

            AddComponent(entity, new Enemy());
            // Velocity starts at zero; the two feel knobs are 0 = inherit the global settings.
            AddComponent(entity, new FlowFieldFollowerMovement
            {
                Acceleration = authoring.Acceleration,
                MaxTurnRateDeg = authoring.MaxTurnRateDeg
            });
            AddComponent(entity, new RunScope());

            if (authoring.UseSnappedMovement)
                AddComponent<HardSnappedMovement>(entity);

            AddComponent(entity, new Health { Value = (int)authoring.BaseStats.MaxHealth });

            AddBuffer<EnemySpellReady>(entity);
            AddBuffer<DamageBufferElement>(entity);
            // Heal channel (invariant: every damageable entity carries a heal buffer). Empty for now;
            // future enemy healers / charmed allies / heal-capable summons built on this stack use it.
            AddBuffer<HealBufferElement>(entity);

            AddComponent(entity, new DamageOnContact
            {
                Damage = authoring.ContactDamage,
                TargetLayers = CollisionLayers.Player | CollisionLayers.Obstacle,
                Tags = ESpellTag.None,
                TotalCritChance = 0,
                TotalCritMultiplier = 1,
            });
            SetComponentEnabled<DamageOnContact>(entity, true);
            AddBuffer<HitEntityMemory>(entity);

            // NOTE: ActiveKnockback is already pre-added (disabled) by ActiveEffectsAuthoring, together
            // with the other effect markers (Burn/Stun/Slow). We rely on that so the dash / collision
            // systems only Set + Enable — no structural change means no ECB crash on same-frame death.

            // Dash chain-damage marker: off until a chain-damage dash knocks this enemy (see DashChainDamageSystem).
            AddComponent<DashChainDamage>(entity);
            SetComponentEnabled<DashChainDamage>(entity, false);

            AddComponent(entity, new CoreStats
            {
                // Bases
                MaxHealth = authoring.BaseStats.MaxHealth,
                BaseArmor = authoring.BaseStats.BaseArmor,
                BaseMoveSpeed = authoring.BaseStats.BaseMoveSpeed,
                BasePickupRange = authoring.BaseStats.BasePickupRange,

                HealthRegen = authoring.BaseStats.HealthRegen,
                Armor = authoring.BaseStats.Armor,
                KnockbackResistance = authoring.BaseStats.KnockbackResistance,
                MoveSpeed = authoring.BaseStats.MoveSpeed,
                PickupRange = authoring.BaseStats.PickupRange,

                Damage = authoring.BaseStats.Damage,
                AttackSpeed = authoring.BaseStats.AttackSpeed,
                SpellSize = authoring.BaseStats.SpellSize,
                SpellSpeed = authoring.BaseStats.SpellSpeed,
                SpellDuration = authoring.BaseStats.SpellDuration,
                CastRange = authoring.BaseStats.CastRange,

                Amount = authoring.BaseStats.Amount,
                Pierce = authoring.BaseStats.Pierce,
                Bounce = authoring.BaseStats.Bounce,

                CritChance = authoring.BaseStats.CritChance,
                CritDamage = authoring.BaseStats.CritDamage,

                DashCount = authoring.BaseStats.DashCount,
                DashCooldown = authoring.BaseStats.DashCooldown,
            });

            AddComponent(entity, new FinalStats());

            // todo virer ça et utiliser lookup de spell modifier dans spell calculation system
            AddBuffer<SpellModifier>(entity);

            AddBuffer<ActiveSpell>(entity);
            DynamicBuffer<SpellActivationRequest> baseSpellBuffer = AddBuffer<SpellActivationRequest>(entity);

            if (authoring.InitialSpells != null)
            {
                foreach (var spellSO in authoring.InitialSpells)
                {
                    if (spellSO == null) continue;

                    baseSpellBuffer.Add(new SpellActivationRequest
                    {
                        ID = spellSO.ID,
                    });
                }
            }

            // var hitColor = new HitFrameFeedbackSystem.HitFrameColor { Value = 0 };
            // AddComponent(entity, hitColor);
            // SetComponentEnabled<HitFrameFeedbackSystem.HitFrameColor>(entity, false);
        }
    }
}