using Unity.Entities;
using UnityEngine;

public class EnemyAuthoring : MonoBehaviour
{
    [Header("Movement precision")]
    [Tooltip("If true, the enemy will be snapped perfectly on the ground following the terrain height. Otherwise, it will follow the base radius.")]
    public bool UseSnappedMovement = true;

    //[Tooltip("The distance at which the enemy will stop following the target.")]
    //public float StopDistance = 1f;

    [Header("Stats \n" +
        "Resistances are value in %.")]
    public BaseStats BaseStats;

    [Header("Spells")]
    public SpellDataSO[] InitialSpells;

    [Header("Modifiers")]
    public StatModifier[] InitialModifers;

    private class Baker : Baker<EnemyAuthoring>
    {
        public override void Bake(EnemyAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new Enemy() { });

            AddComponent(entity, new FollowTargetMovement() { Speed = authoring.BaseStats.MoveSpeed });

            AddComponent(entity, new RunScope() { });


            if (authoring.UseSnappedMovement)
                AddComponent<HardSnappedMovement>(entity);

            AddComponent(entity, new Health() { Value = authoring.BaseStats.MaxHealth });

            AddBuffer<EnemySpellReady>(entity);

            AddComponent(entity, authoring.BaseStats);

            AddComponent(entity, new Stats()
            {
                MaxHealth = authoring.BaseStats.MaxHealth,
                MoveSpeed = authoring.BaseStats.MoveSpeed,
                Damage = authoring.BaseStats.Damage,
                Armor = authoring.BaseStats.Armor,
                CooldownReduction = authoring.BaseStats.CooldownReduction,
                CritChance = authoring.BaseStats.CritChance,
                CritMultiplier = authoring.BaseStats.CritMultiplier
            });

            DynamicBuffer<StatModifier> modifierBuffer = AddBuffer<StatModifier>(entity);
            foreach (var modifier in authoring.InitialModifers)
            {
                modifierBuffer.Add(modifier);
            }

            AddComponent<RecalculateStatsRequest>(entity);

            AddBuffer<DamageBufferElement>(entity);

            DynamicBuffer<ActiveSpell> spellBuffer = AddBuffer<ActiveSpell>(entity);

            DynamicBuffer<SpellActivationRequest> baseSpellBuffer = AddBuffer<SpellActivationRequest>(entity);
            foreach (var spellSO in authoring.InitialSpells)
            {
                if (spellSO == null)
                    continue;

                baseSpellBuffer.Add(new SpellActivationRequest
                {
                    ID = spellSO.ID,
                });
            }

            // HitFrame feedback component
            var hitColor = new HitFrameFreedbackSystem.HitFrameColor { Value = 0 };
            AddComponent(entity, hitColor);
            SetComponentEnabled<HitFrameFreedbackSystem.HitFrameColor>(entity, false);
        }
    }
}
