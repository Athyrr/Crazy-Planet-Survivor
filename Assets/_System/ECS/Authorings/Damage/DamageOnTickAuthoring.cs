using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class DamageOnTickAuthoring : MonoBehaviour
{
    [Header("Damage")]
    public float DamagePerTick = 10f;
    public float TickRate = 1f;

    [Header("Shape")]
    [Tooltip("Base radius of the visual. Runtime = BaseSize × this (scaled by size upgrades).")]
    public float AreaRadius = 1f;
    [Tooltip("Circle = simple radius. Cone/Ring use shape filtering.")]
    public EAttackAreaShape Shape = EAttackAreaShape.Circle;

    [Tooltip("Cone aperture half-angle in degrees (e.g., 30 = 60° cone)")]
    public float HalfAngle = 30f;
    [Tooltip("Cone sweep start relative to forward (degrees)")]
    public float SweepStart;
    [Tooltip("Cone sweep end relative to forward (degrees)")]
    public float SweepEnd;

    [Tooltip("Ring mode: width of the ring band")]
    public float RingThickness = 0.5f;

    class Baker : Baker<DamageOnTickAuthoring>
    {
        public override void Bake(DamageOnTickAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new DamageOnTick
            {
                AreaRadius = authoring.AreaRadius,
                PrefabRadius = authoring.AreaRadius,
                Shape = authoring.Shape,
                HalfAngle = math.radians(authoring.HalfAngle),
                SweepStart = math.radians(authoring.SweepStart),
                SweepEnd = math.radians(authoring.SweepEnd),
                RingThickness = authoring.RingThickness,
            });
        }
    }


#if UNITY_EDITOR
    private static readonly Color GizmoColor = new Color(1f, 0.5f, 0f, 0.7f);
    private static readonly Color GizmoDim = new Color(1f, 0.35f, 0f, 0.35f);

    private void OnDrawGizmosSelected()
    {
        DrawShapePreview();
        DrawInfoLabel();
    }

    private void DrawInfoLabel()
    {
        var style = new GUIStyle { normal = new GUIStyleState { textColor = GizmoColor } };
        string label = $"{Shape} | R≈{AreaRadius:F1} (×BaseSize) | Tick={TickRate:F1}s | Dmg={DamagePerTick:F0}";
        if (Shape == EAttackAreaShape.Cone)
            label += $" | HalfAngle={HalfAngle:F0}°";
        if (Shape == EAttackAreaShape.Ring)
            label += $" | Thickness={RingThickness:F2}";
        UnityEditor.Handles.Label(transform.position + Vector3.up * 2f, label, style);
    }

    private void DrawShapePreview()
    {
        Vector3 pos = transform.position;
        float radius = AreaRadius;

        switch (Shape)
        {
            case EAttackAreaShape.Circle:
                DrawWireCircle(pos, radius, GizmoColor, 32);
                break;

            case EAttackAreaShape.Cone:
            {
                Vector3 fwd = transform.forward;
                float halfAngleRad = HalfAngle * Mathf.Deg2Rad;

                // Cone boundary lines
                Gizmos.color = GizmoColor;
                Quaternion leftRot = Quaternion.AngleAxis(-HalfAngle, transform.up);
                Quaternion rightRot = Quaternion.AngleAxis(HalfAngle, transform.up);
                Vector3 leftDir = leftRot * fwd * radius;
                Vector3 rightDir = rightRot * fwd * radius;

                Gizmos.DrawRay(pos, leftDir);
                Gizmos.DrawRay(pos, rightDir);

                // Arc at the end
                UnityEditor.Handles.color = GizmoColor;
                UnityEditor.Handles.DrawWireArc(pos, transform.up, leftDir.normalized, HalfAngle * 2f, radius);

                // Center forward direction (dim)
                Gizmos.color = GizmoDim;
                Gizmos.DrawRay(pos, fwd * radius);
                break;
            }

            case EAttackAreaShape.Ring:
            {
                float innerRadius = math.max(0f, radius - RingThickness * 0.5f);
                float outerRadius = radius + RingThickness * 0.5f;

                DrawWireCircle(pos, innerRadius, GizmoDim, 32);
                DrawWireCircle(pos, outerRadius, GizmoColor, 32);

                // Cross-lines between inner and outer to show ring band
                Gizmos.color = GizmoDim;
                for (int i = 0; i < 8; i++)
                {
                    float angle = i * 45f * Mathf.Deg2Rad;
                    Vector3 dir = new Vector3(Mathf.Sin(angle), 0f, Mathf.Cos(angle));
                    Gizmos.DrawLine(pos + dir * innerRadius, pos + dir * outerRadius);
                }
                break;
            }
        }
    }

    /// <summary>Draw a wireframe circle at a position.</summary>
    private static void DrawWireCircle(Vector3 center, float radius, Color color, int segments)
    {
        Gizmos.color = color;
        float angleStep = 2f * Mathf.PI / segments;
        Vector3 prev = center + new Vector3(radius, 0f, 0f);

        for (int i = 1; i <= segments; i++)
        {
            float angle = i * angleStep;
            Vector3 next = center + new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
            Gizmos.DrawLine(prev, next);
            prev = next;
        }
    }
#endif
}
