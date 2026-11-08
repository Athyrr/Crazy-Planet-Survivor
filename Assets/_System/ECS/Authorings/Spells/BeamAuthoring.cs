using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

/// <summary>FORM of a channeled-beam ray (spec channeled-beam, decision C5): turn rate, visual origin height and the
/// visual prefab. Goes on the ray prefab next to DamageOnTickAuthoring (Shape = Capsule), SpellSourceAuthoring and
/// RunScopeAuthoring. The POWER comes from the root spell, copied every frame by BeamAimSystem.</summary>
public class BeamAuthoring : MonoBehaviour
{
    [Tooltip("Max turn rate of the beam toward its target, degrees per second.")]
    [Min(0f)] public float TurnRateDegrees = 90f;

    [Tooltip("Visual only: both beam ends raised by this height along the local normal (m). No effect on the hit.")]
    [Min(0f)] public float OriginHeight = 1.2f;

    [Tooltip("Visual prefab with a BeamVisual at its root (e.g. PF_VFX_Beam_Holy). Never converted to an entity.")]
    public GameObject VisualPrefab;

    private class Baker : Baker<BeamAuthoring>
    {
        public override void Bake(BeamAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new BeamAim
            {
                TurnRateRadians = math.radians(authoring.TurnRateDegrees),
                OriginHeight = authoring.OriginHeight,
                Direction = new float3(0f, 0f, 1f),
            });
            AddComponent(entity, new AreaSegment());

            // Born off: no tick before BeamAimSystem gives the ray a target (ECB Instantiate keeps the bit).
            AddComponent<BeamActive>(entity);
            SetComponentEnabled<BeamActive>(entity, false);

            // Asset reference only — never GetEntity on it (it would become an entity).
            AddComponent(entity, new BeamVisualPrefab { Value = authoring.VisualPrefab });
        }
    }
}
