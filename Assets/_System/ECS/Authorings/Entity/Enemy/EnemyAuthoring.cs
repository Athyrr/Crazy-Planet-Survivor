using Unity.Entities;
using UnityEngine;

[RequireComponent(typeof(EntityAuthoring))]
public class EnemyAuthoring : MonoBehaviour
{
    public Renderer MainRenderer;
    
    [Header("Movement precision")]
    [Tooltip(
        "If true, the enemy will be snapped perfectly on the ground following the terrain height. Otherwise, it will follow the base radius.")]
    public bool UseSnappedMovement = true;

    [Header("Stats")] public CoreStats BaseStats;

    [Header("Spells")] public SpellDataSO[] InitialSpells;

    private class Baker : Baker<EnemyAuthoring>
    {
        public override void Bake(EnemyAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            if (authoring.MainRenderer != null)
                AddComponentObject(entity, new VisualRendererLink { Renderer = authoring.MainRenderer });
            
            AddComponent(entity, new Enemy());
            AddComponent(entity, new FollowTargetMovement { Speed = authoring.BaseStats.BaseMoveSpeed });
            AddComponent(entity, new RunScope());

            if (authoring.UseSnappedMovement)
                AddComponent<HardSnappedMovement>(entity);

            AddComponent(entity, new Health { Value = (int)authoring.BaseStats.BaseMaxHealth });

            AddBuffer<EnemySpellReady>(entity);
            AddBuffer<DamageBufferElement>(entity);

            AddComponent(entity, new CoreStats
            {
                // Bases
                BaseMaxHealth = authoring.BaseStats.BaseMaxHealth,
                BaseArmor = authoring.BaseStats.BaseArmor,
                BaseMoveSpeed = authoring.BaseStats.BaseMoveSpeed,
                BasePickupRange = authoring.BaseStats.BasePickupRange,

                MaxHealthMultiplier = authoring.BaseStats.MaxHealthMultiplier,
                HealthRecovery = authoring.BaseStats.HealthRecovery,
                DamageReductionMultiplier = authoring.BaseStats.DamageReductionMultiplier,
                MoveSpeedMultiplier = authoring.BaseStats.MoveSpeedMultiplier,
                PickupRangeMultiplier = authoring.BaseStats.PickupRangeMultiplier,

                GlobalDamageMultiplier = authoring.BaseStats.GlobalDamageMultiplier,
                GlobalCooldownMultiplier = authoring.BaseStats.GlobalCooldownMultiplier,
                GlobalSpellAreaMultiplier = authoring.BaseStats.GlobalSpellAreaMultiplier,
                GlobalSpellSizeMultiplier = authoring.BaseStats.GlobalSpellSizeMultiplier,
                GlobalSpellSpeedMultiplier = authoring.BaseStats.GlobalSpellSpeedMultiplier,
                GlobalDurationMultiplier = authoring.BaseStats.GlobalDurationMultiplier,
                GlobalCastRangeMultiplier = authoring.BaseStats.GlobalCastRangeMultiplier,

                GlobalAmountBonus = authoring.BaseStats.GlobalAmountBonus,
                GlobalPierceBonus = authoring.BaseStats.GlobalPierceBonus,
                GlobalBounceBonus = authoring.BaseStats.GlobalBounceBonus,

                CritChance = authoring.BaseStats.CritChance,
                CritDamageMultiplier = authoring.BaseStats.CritDamageMultiplier,
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

            var hitColor = new HitFrameFeedbackSystem.HitFrameColor { Value = 0 };
            AddComponent(entity, hitColor);
            SetComponentEnabled<HitFrameFeedbackSystem.HitFrameColor>(entity, false);
        }
    }
}