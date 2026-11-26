using Unity.Entities;
using UnityEngine;

/// <summary>
/// Enemy structure. The values live on the <see cref="EnemySO"/> referenced by <see cref="Config"/> and are baked
/// from its resolved version (parent chain applied, see <see cref="DefinitionResolver"/>).
/// </summary>
[RequireComponent(typeof(DestructibleAuthoring))]
public class EnemyAuthoring : MonoBehaviour
{
    [Tooltip("Enemy definition (values), resolved through its parent chain at bake time. A boss prefab points at its BossSO here.")]
    public EnemySO Config;

    public Renderer MainRenderer;

    private class Baker : Baker<EnemyAuthoring>
    {
        public override void Bake(EnemyAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            if (authoring.Config == null)
                Debug.LogError($"[EnemyAuthoring] {authoring.name}: no EnemySO assigned to Config, baking the enemy " +
                               "archetype with default values.", authoring);

            // Resolved copy (parent chain applied), destroyed at the end of Bake. DependsOn is declared on every link.
            using var resolved = DefinitionResolver.Resolve(authoring.Config, this);
            EnemySO config = resolved.Value;

            // Fallbacks mirror the former EnemyAuthoring field initializers.
            bool useSnappedMovement = config == null || config.UseSnappedMovement;
            float acceleration = config != null ? config.Acceleration : 0f;
            float maxTurnRateDeg = config != null ? config.MaxTurnRateDeg : 0f;
            CoreStats baseStats = config != null ? config.BaseStats : default;
            SpellSO[] initialSpells = config != null ? config.InitialSpells : null;
            float contactDamage = config != null ? config.ContactDamage : 10f;

            AddComponent(entity, new Enemy());
            AddComponent(entity, new Team { Value = ETeam.Enemy });
            // Velocity starts at zero; the two feel knobs are 0 = inherit the global settings.
            AddComponent(entity, new FlowFieldFollowerMovement
            {
                Acceleration = acceleration,
                MaxTurnRateDeg = maxTurnRateDeg
            });
            AddComponent(entity, new RunScope());

            if (useSnappedMovement)
                AddComponent<HardSnappedMovement>(entity);

            AddComponent(entity, new Health { Value = (int)baseStats.MaxHealth });

            AddBuffer<EnemySpellReady>(entity);
            AddBuffer<DamageBufferElement>(entity);
            // Heal channel (invariant: every damageable entity carries a heal buffer). Empty for now;
            // future enemy healers / charmed allies / heal-capable summons built on this stack use it.
            AddBuffer<HealBufferElement>(entity);

            AddComponent(entity, new DamageOnContact
            {
                Damage = contactDamage,
                TargetLayers = CollisionLayers.Player | CollisionLayers.Obstacle,
                Tags = ESpellTag.None,
                TotalCritChance = 0,
                TotalCritMultiplier = 1,
            });
            SetComponentEnabled<DamageOnContact>(entity, true);

            AddBuffer<HitEntityMemory>(entity);

            // NOTE: KnockbackState is already pre-added (disabled) by ActiveEffectsAuthoring, together
            // with the other status-effect mirrors (BurnState/StunState/SlowState). We rely on that so the
            // dash / collision systems only Set + Enable — no structural change means no ECB crash on
            // same-frame death.

            // Dash chain-damage marker: off until a chain-damage dash knocks this enemy (see DashChainDamageSystem).
            AddComponent<DashChainDamage>(entity);
            SetComponentEnabled<DashChainDamage>(entity, false);

            // Field-by-field copy on purpose: the 11 stats not listed here stay 0 on enemies.
            AddComponent(entity, new CoreStats
            {
                // Bases
                MaxHealth = baseStats.MaxHealth,
                BaseArmor = baseStats.BaseArmor,
                BaseMoveSpeed = baseStats.BaseMoveSpeed,
                BasePickupRange = baseStats.BasePickupRange,

                HealthRegen = baseStats.HealthRegen,
                Armor = baseStats.Armor,
                KnockbackResistance = baseStats.KnockbackResistance,
                MoveSpeed = baseStats.MoveSpeed,
                PickupRange = baseStats.PickupRange,

                Damage = baseStats.Damage,
                AttackSpeed = baseStats.AttackSpeed,
                SpellSize = baseStats.SpellSize,
                SpellSpeed = baseStats.SpellSpeed,
                SpellDuration = baseStats.SpellDuration,
                CastRange = baseStats.CastRange,

                Amount = baseStats.Amount,
                Pierce = baseStats.Pierce,
                Bounce = baseStats.Bounce,

                CritChance = baseStats.CritChance,
                CritDamage = baseStats.CritDamage,

                DashCount = baseStats.DashCount,
                DashCooldown = baseStats.DashCooldown,
            });

            AddComponent(entity, new LiveStats());
            // Temporary stat buffs on enemies (debuffs from player spells, or future ally buffs).
            AddBuffer<CharacterStatBuff>(entity);

            // todo: remove this and use the spell modifier lookup in the spell calculation system
            AddBuffer<SpellStatUpgrade>(entity);

            AddBuffer<ActiveSpell>(entity);
            DynamicBuffer<SpellActivationRequest> baseSpellBuffer = AddBuffer<SpellActivationRequest>(entity);

            if (initialSpells != null)
            {
                foreach (var spellSO in initialSpells)
                {
                    if (spellSO == null) continue;

                    // The baker reads the spell's ID and Emitters: editing the spell asset must rebake the enemy.
                    DependsOn(spellSO);

                    if (spellSO.Emitters != null && spellSO.Emitters.Length > 0)
                        Debug.LogError($"[EnemyAuthoring] {authoring.name}: spell '{spellSO.name}' declares emitters — " +
                                       "emitters are only fired by player casts, they will never fire on an enemy.", authoring);

                    baseSpellBuffer.Add(new SpellActivationRequest
                    {
                        ID = spellSO.ID,
                    });
                }
            }
        }
    }
}
