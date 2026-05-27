using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class AreaAttackAuthoring : MonoBehaviour
{
    public EAttackAreaShape Shape = EAttackAreaShape.Cone;

    [Header("Shape Parameters")]
    public float RadiusStart = 1f;
    public float RadiusEnd = 1f;

    [Tooltip("Cone aperture half-angle in degrees (e.g., 45 = 90° total cone)")]
    public float HalfAngle = 45f;
    [Tooltip("Cone sweep start rotation relative to forward (degrees)")]
    public float SweepStart;
    [Tooltip("Cone sweep end rotation relative to forward (degrees)")]
    public float SweepEnd;

    [Tooltip("Ring mode: width of the ring band")]
    public float RingThickness = 0.5f;

    [Header("Timing")]
    [Tooltip("Delay before collision evaluation starts")]
    public float ActivationDelay;
    [Tooltip("How long collision evaluation runs")]
    public float ActiveDuration = 0.5f;

    class Baker : Baker<AreaAttackAuthoring>
    {
        public override void Bake(AreaAttackAuthoring authoring)
        {
            var entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new AreaAttack
            {
                Shape = authoring.Shape,

                RadiusStart = authoring.RadiusStart,
                RadiusEnd = authoring.RadiusEnd,

                HalfAngle = math.radians(authoring.HalfAngle),
                SweepStart = math.radians(authoring.SweepStart),
                SweepEnd = math.radians(authoring.SweepEnd),

                RingThickness = authoring.RingThickness,

                ActivationDelay = authoring.ActivationDelay,
                ActiveDuration = authoring.ActiveDuration,
                ElapsedTime = 0f,

                Damage = 0f,
                CritChance = 0f,
                CritMultiplier = 0f,
                Caster = Entity.Null,
                TargetLayers = 0,
                Tags = 0,
            });

            AddBuffer<HitEntityMemory>(entity);
        }
    }

    // Gizmos

#if UNITY_EDITOR
    private static readonly Color GizmoColor = new Color(0f, 1f, 1f, 0.8f);
    private static readonly Color GizmoColorDim = new Color(0f, 0.7f, 0.7f, 0.4f);
    private static readonly Color SweepArcColor = new Color(1f, 0.8f, 0f, 0.7f);

    private static readonly int TimelineSteps = 4; // t = 0/4, 1/4, 2/4, 3/4, 4/4

    private void OnDrawGizmosSelected()
    {
        if (ActiveDuration < 0.01f)
        {
            // One-shot: just draw the final shape at ActivationDelay
            DrawShapeAtTime(1f, GizmoColor, false);
            DrawTimingLabel($"ActivationDelay={ActivationDelay:F2}s");
            return;
        }

        // Timeline: draw shape at multiple time steps over ActiveDuration
        for (int i = 0; i <= TimelineSteps; i++)
        {
            float t = (float)i / TimelineSteps;
            float alpha = 0.2f + 0.8f * t; // fades in over time
            Color color = new Color(0f, 1f, 1f, alpha);
            DrawShapeAtTime(t, color, i == TimelineSteps);
        }

        float totalDuration = ActivationDelay + ActiveDuration;
        DrawTimingLabel($"Delay={ActivationDelay:F2}s + Active={ActiveDuration:F2}s = {totalDuration:F2}s total");
    }

    private void DrawTimingLabel(string text)
    {
#if UNITY_EDITOR
        var style = new GUIStyle { normal = new GUIStyleState { textColor = Color.cyan } };
        UnityEditor.Handles.Label(transform.position + Vector3.up * 2f, text, style);
#endif
    }

    private void DrawShapeAtTime(float t, Color color, bool isFinal)
    {
        float radius = math.lerp(RadiusStart, RadiusEnd, t);
        float sweep = math.lerp(SweepStart, SweepEnd, t);

        switch (Shape)
        {
            case EAttackAreaShape.Circle:
                DrawCirclePreview(radius, color);
                break;
            case EAttackAreaShape.Cone:
                DrawConeShapeAt(radius, sweep, color, isFinal);
                break;
            case EAttackAreaShape.Ring:
                DrawRingPreview(radius, color);
                break;
        }
    }

    private void DrawCirclePreview(float radius, Color color)
    {
        Gizmos.color = color;
        Gizmos.DrawWireSphere(transform.position, radius);
    }

    private void DrawConeShapeAt(float radius, float sweepDeg, Color color, bool isFinal)
    {
        Vector3 pos = transform.position;
        Vector3 fwd = transform.forward;
        Vector3 up = transform.up;

        if (isFinal)
        {
            // Forward reference
            Gizmos.color = Color.white;
            Gizmos.DrawLine(pos, pos + fwd * radius * 0.3f);

            // Sweep arc (path of the cone center over time)
            float arcLen = math.abs(SweepEnd - SweepStart);
            if (arcLen > 0.5f)
            {
                Vector3 startDir = Quaternion.AngleAxis(SweepStart, up) * fwd;
                UnityEditor.Handles.color = SweepArcColor;
                UnityEditor.Handles.DrawWireArc(pos, up, startDir, SweepEnd - SweepStart, radius * 0.4f);
            }
        }

        DrawConeShape(pos, fwd, up, radius, HalfAngle, sweepDeg, color);
    }

    private static void DrawConeShape(Vector3 pos, Vector3 fwd, Vector3 up,
        float radius, float halfAngleDeg, float sweepDeg, Color color)
    {
        Vector3 dir = Quaternion.AngleAxis(sweepDeg, up) * fwd;

        Vector3 axis = Vector3.Cross(up, dir).normalized;
        if (axis.sqrMagnitude < 0.001f)
            axis = Vector3.Cross(Vector3.forward, dir).normalized;

        float halfRad = halfAngleDeg * Mathf.Deg2Rad;
        float cosA = Mathf.Cos(halfRad);
        float sinA = Mathf.Sin(halfRad);

        Vector3 arcCenter = pos + dir * radius * cosA;
        float arcRadius = radius * sinA;

        Vector3 leftDir = Quaternion.AngleAxis(-halfAngleDeg, axis) * dir;
        Vector3 rightDir = Quaternion.AngleAxis(halfAngleDeg, axis) * dir;

        Gizmos.color = color;
        Gizmos.DrawLine(pos, pos + leftDir * radius);
        Gizmos.DrawLine(pos, pos + rightDir * radius);

        Vector3 fromDir = Quaternion.AngleAxis(-halfAngleDeg, axis) * dir;
        UnityEditor.Handles.color = color;
        UnityEditor.Handles.DrawWireArc(arcCenter, dir, fromDir, halfAngleDeg * 2f, arcRadius);
    }

    private void DrawRingPreview(float radius, Color color)
    {
        float halfThick = RingThickness * 0.5f;

        Gizmos.color = color;
        Gizmos.DrawWireSphere(transform.position, radius + halfThick);

        Gizmos.color = new Color(color.r, color.g, color.b, color.a * 0.5f);
        Gizmos.DrawWireSphere(transform.position, math.max(0f, radius - halfThick));
    }
#endif
}
