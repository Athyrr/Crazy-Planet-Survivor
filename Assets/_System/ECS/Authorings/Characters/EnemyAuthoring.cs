using Unity.Entities;
using UnityEngine;

public class EnemyAuthoring : MonoBehaviour
{
    [Header("Movement precision")]
    [Tooltip(
        "If true, the enemy will be snapped perfectly on the ground following the terrain height. Otherwise, it will follow the base radius.")]
    public bool UseSnappedMovement = true;

    [Header("Stats")] public Stats BaseStats;

    [Header("Spells")] public SpellDataSO[] InitialSpells;

    private class Baker : Baker<EnemyAuthoring>
    {
        public override void Bake(EnemyAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new Enemy());
            AddComponent(entity, new FollowTargetMovement { Speed = authoring.BaseStats.BaseMoveSpeed });
            AddComponent(entity, new RunScope());

            if (authoring.UseSnappedMovement)
                AddComponent<HardSnappedMovement>(entity);

            AddComponent(entity, new Health { Value = (int)authoring.BaseStats.BaseMaxHealth });

            AddBuffer<EnemySpellReady>(entity);
            AddBuffer<DamageBufferElement>(entity);

            AddComponent(entity, new Stats
            {
                // Bases
                BaseMaxHealth = authoring.BaseStats.BaseMaxHealth,
                BaseArmor = authoring.BaseStats.BaseArmor,
                BaseMoveSpeed = authoring.BaseStats.BaseMoveSpeed,
                BasePickupRange = authoring.BaseStats.BasePickupRange,

                MaxHealthMultiplier = 1.0f,
                HealthRecovery = authoring.BaseStats.HealthRecovery,
                DamageReductionMultiplier = 1.0f,
                MoveSpeedMultiplier = 1.0f,
                PickupRangeMultiplier = 1.0f,

                GlobalDamageMultiplier = 1.0f,
                GlobalCooldownMultiplier = 1.0f,
                GlobalSpellAreaMultiplier = 1.0f,
                GlobalSpellSizeMultiplier = 1.0f,
                GlobalSpellSpeedMultiplier = 1.0f,
                GlobalDurationMultiplier = 1.0f,
                GlobalCastRangeMultiplier = 1.0f,

                GlobalAmountBonus = 0,
                GlobalPierceBonus = 0,
                GlobalBounceBonus = 0,

                CritChance = authoring.BaseStats.CritChance,
                CritDamageMultiplier = authoring.BaseStats.CritDamageMultiplier,
            });

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

            var hitColor = new HitFrameFeedbackSystem.HitFrameColor { Value = 0 };
            AddComponent(entity, hitColor);
            SetComponentEnabled<HitFrameFeedbackSystem.HitFrameColor>(entity, false);
        }
    }
}