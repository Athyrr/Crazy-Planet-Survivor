using Unity.Entities;
using UnityEngine;

/// <summary>Training dummy, on top of an EnemyAuthoring (Enemy, Health, CoreStats, LiveStats, buffers,
/// RunScope). Adds DummyTag (excluded from kill_all and the enemy modes), Frozen (no own movement), DamageTally
/// (sandbox measures) and HealthFloor (immortal: floor 1 HP, full heal RegenDelay s after the last hit).
/// Always compiled: baked type.</summary>
[RequireComponent(typeof(EnemyAuthoring))]
public class DummyAuthoring : MonoBehaviour
{
    [Tooltip("Seconds without a damaging hit before the dummy heals back to full.")]
    public float RegenDelay = 3f;

    private class Baker : Baker<DummyAuthoring>
    {
        public override void Bake(DummyAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent<DummyTag>(entity);
            AddComponent<Frozen>(entity);
            AddComponent<DamageTally>(entity);
            AddComponent(entity, new HealthFloor
            {
                Floor = 1,
                RegenDelay = authoring.RegenDelay,
            });
        }
    }
}
