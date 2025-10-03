using Unity.Collections;
using Unity.Entities;
using UnityEngine;

public class EnemyAuthoring : MonoBehaviour
{
    [Header("Stats")]
    public BaseStats BaseStats = new BaseStats()
    {
        MaxHealth = 100,
        Damage = 100,
        Armor = 5,
        Speed = 2
    };

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

            DynamicBuffer<ActiveSpell> spellBuffer = AddBuffer<ActiveSpell>(entity);

            DynamicBuffer<BaseSpell> baseSpellBuffer = AddBuffer<BaseSpell>(entity);
            foreach (var spellSO in authoring.InitialSpells)
            {
                if (spellSO == null)
                    continue;

                baseSpellBuffer.Add(new BaseSpell
                {
                    ID = spellSO.ID,
                });
            }
        }
    }
}
