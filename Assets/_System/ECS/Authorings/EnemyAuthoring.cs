using Unity.Entities;
using UnityEngine;

public class EnemyAuthoring : MonoBehaviour
{
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

            AddComponent(entity, new Health() { Value = authoring.BaseStats.MaxHealth });

            AddComponent(entity, new FollowTargetMovement() { Speed = authoring.BaseStats.Speed });

            AddBuffer<EnemySpellReady>(entity);

            AddComponent(entity, authoring.BaseStats);

            AddComponent(entity, new Stats()
            {
                MaxHealth = authoring.BaseStats.MaxHealth,
                Speed = authoring.BaseStats.Speed,
                Damage = authoring.BaseStats.Damage,
                Armor = authoring.BaseStats.Armor,
                CooldownReduction = authoring.BaseStats.CooldownReduction
            });

            DynamicBuffer<StatModifier> modifierBuffer = AddBuffer<StatModifier>(entity);
            foreach (var modifier in authoring.InitialModifers)
            {
                modifierBuffer.Add(modifier);
            }

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
        }
    }
}
