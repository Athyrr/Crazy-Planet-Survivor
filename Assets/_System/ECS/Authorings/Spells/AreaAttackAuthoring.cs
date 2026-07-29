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

    [Header("Placement")]
    [Tooltip("Local offset of the hitbox from the entity (entity-space, points toward the target). " +
             "Set this to match a forward-offset visual so collision and VFX line up.")]
    public Vector3 Offset;

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
                Cadence = EZoneCadence.Burst,
                Shape = authoring.Shape,

                RadiusStart = authoring.RadiusStart,
                RadiusEnd = authoring.RadiusEnd,

                HalfAngle = math.radians(authoring.HalfAngle),
                SweepStart = math.radians(authoring.SweepStart),
                SweepEnd = math.radians(authoring.SweepEnd),

                RingThickness = authoring.RingThickness,
                Offset = authoring.Offset,

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

    // ── Gizmos ──
    // Faithful preview of what AreaAttackSystem actually queries at runtime:
    //   • Hitbox center = transform.position + rotation × Offset   (rotation+scale aware; entity faces target in-game)
    //   • Timeline      = successive t = 0/N…N/N over ActiveDuration
    //                     · Circle/Cone/Ring: radius = lerp(RadiusStart, RadiusEnd, t) ; Cone sweep = lerp(Sweep…)
    //                     · Capsule (progressive thrust): segment [caster → lerp(caster, offset endpoint, t)],
    //                       half-width = RadiusStart. At t=0 it's a sphere on the caster; at t=1 the full estoc.

#if UNITY_EDITOR
    private static readonly Color CasterMarkerColor = new Color(1f, 1f, 1f, 0.6f);
    private static readonly Color OffsetLinkColor   = new Color(1f, 1f, 1f, 0.3f);
    private static readonly Color SweepArcColor     = new Color(1f, 0.8f, 0f, 0.7f);
    private static readonly Color FinalShapeColor   = new Color(0f, 1f, 1f, 1f);

    private const int TimelineSteps = 4; // t = 0/4, 1/4, 2/4, 3/4, 4/4

    private void OnDrawGizmosSelected()
    {
        Vector3 casterPos     = transform.position;
        Quaternion rot        = transform.rotation;
        Vector3 up            = transform.up;
        Vector3 hitboxCenter  = casterPos + rot * Offset;

        // Caster origin marker (small circle at the entity origin — the anchor).
        Gizmos.color = CasterMarkerColor;
        Gizmos.DrawWireSphere(casterPos, 0.15f);

        // Link caster → hitbox center when the Offset displaces the shape (Capsule already shows this via its segment).
        if (Offset.sqrMagnitude > 0.0001f && Shape != EAttackAreaShape.Capsule)
        {
            Gizmos.color = OffsetLinkColor;
            Gizmos.DrawLine(casterPos, hitboxCenter);
        }

        // Static shape (no active window): draw the final state only.
        if (ActiveDuration < 0.01f)
        {
            DrawShapeAtTime(1f, FinalShapeColor, true, casterPos, hitboxCenter, rot, up);
            DrawInfoLabel(casterPos, $"{Shape} | ActivationDelay={ActivationDelay:F2}s (one-shot)");
            return;
        }

        // Timeline of the active window — alpha ramps 0.2 → 1.0 to show progression.
        for (int i = 0; i <= TimelineSteps; i++)
        {
            float t = (float)i / TimelineSteps;
            var color = new Color(0f, 1f, 1f, 0.2f + 0.8f * t);
            DrawShapeAtTime(t, color, i == TimelineSteps, casterPos, hitboxCenter, rot, up);
        }

        DrawInfoLabel(casterPos, BuildInfoLabel());
    }

    private string BuildInfoLabel()
    {
        string s = $"{Shape}  |  Delay={ActivationDelay:F2}s + Active={ActiveDuration:F2}s";
        switch (Shape)
        {
            case EAttackAreaShape.Circle:
            case EAttackAreaShape.Cone:
            case EAttackAreaShape.Ring:
                s += $"  |  R {RadiusStart:F1}→{RadiusEnd:F1}";
                break;
            case EAttackAreaShape.Capsule:
                s += $"  |  length {Offset.magnitude:F1}  half-width {RadiusStart:F2}  (progressive)";
                break;
        }
        if (Shape == EAttackAreaShape.Cone)
            s += $"  |  Half {HalfAngle:F0}°  Sweep {SweepStart:F0}→{SweepEnd:F0}°";
        if (Shape == EAttackAreaShape.Ring)
            s += $"  |  Thickness {RingThickness:F2}";
        return s;
    }

    private void DrawInfoLabel(Vector3 anchor, string text)
    {
        var style = new GUIStyle { normal = new GUIStyleState { textColor = Color.cyan } };
        UnityEditor.Handles.Label(anchor + Vector3.up * 2f, text, style);
    }

    private void DrawShapeAtTime(float t, Color color, bool isFinal,
        Vector3 casterPos, Vector3 hitboxCenter, Quaternion rot, Vector3 up)
    {
        float radius = Mathf.Lerp(RadiusStart, RadiusEnd, t);
        float sweep = Mathf.Lerp(SweepStart, SweepEnd, t);

        switch (Shape)
        {
            case EAttackAreaShape.Circle:
                Gizmos.color = color;
                Gizmos.DrawWireSphere(hitboxCenter, radius);
                break;

            case EAttackAreaShape.Cone:
                // Cone apex sits at the hitbox center — matches IsInShape(position, …) in the system.
                DrawConeShape(hitboxCenter, rot * Vector3.forward, up, radius, HalfAngle, sweep, color, isFinal);
                if (isFinal)
                    DrawSweepArc(hitboxCenter, rot * Vector3.forward, up, radius);
                break;

            case EAttackAreaShape.Ring:
                DrawRingPreview(hitboxCenter, radius, color);
                break;

            case EAttackAreaShape.Capsule:
                // Progressive thrust: extend from caster toward the offset endpoint over t.
                Vector3 tipAtT = Vector3.Lerp(casterPos, hitboxCenter, t);
                DrawCapsulePreview(casterPos, tipAtT, RadiusStart, color, up);
                break;
        }
    }

    private void DrawConeShape(Vector3 pos, Vector3 fwd, Vector3 up,
        float radius, float halfAngleDeg, float sweepDeg, Color color, bool isFinal)
    {
        // The runtime cone is 3D-symmetric around coneDir (dot >= cos(HalfAngle)); on a top-down game
        // what matters visually is the horizontal fan on the ground plane. We rotate around `up`
        // (surface normal) so the fan opens sideways — not vertically like the previous gizmo did.
        Vector3 dir = Quaternion.AngleAxis(sweepDeg, up) * fwd;
        Vector3 leftDir  = Quaternion.AngleAxis(-halfAngleDeg, up) * dir;
        Vector3 rightDir = Quaternion.AngleAxis( halfAngleDeg, up) * dir;

        // Fan boundary lines (crisp).
        Gizmos.color = color;
        Gizmos.DrawLine(pos, pos + leftDir  * radius);
        Gizmos.DrawLine(pos, pos + rightDir * radius);

        // Outer arc along the ground plane, from left edge to right edge.
        UnityEditor.Handles.color = color;
        UnityEditor.Handles.DrawWireArc(pos, up, leftDir, halfAngleDeg * 2f, radius);

        if (isFinal)
        {
            // Fill the final fan with a translucent disc slice so the surface reads as a *zone*, not lines.
            var fill = new Color(color.r, color.g, color.b, 0.15f);
            UnityEditor.Handles.color = fill;
            UnityEditor.Handles.DrawSolidArc(pos, up, leftDir, halfAngleDeg * 2f, radius);

            // Direction arrow along the cone axis (short shaft + two barbs) — makes "which way it points" obvious.
            Vector3 tip = pos + dir * radius;
            Vector3 shaftEnd = pos + dir * (radius * 0.9f);
            Vector3 barbLeft  = Quaternion.AngleAxis( 150f, up) * dir;
            Vector3 barbRight = Quaternion.AngleAxis(-150f, up) * dir;
            float barbLen = radius * 0.12f;

            Gizmos.color = Color.white;
            Gizmos.DrawLine(pos, tip);
            Gizmos.DrawLine(tip, tip + barbLeft  * barbLen);
            Gizmos.DrawLine(tip, tip + barbRight * barbLen);

            // Apex marker (small solid dot at the origin of the cone).
            Gizmos.DrawSphere(pos, radius * 0.03f);
        }
    }

    private void DrawSweepArc(Vector3 pos, Vector3 fwd, Vector3 up, float radius)
    {
        // Path the cone center traces over ActiveDuration (SweepStart → SweepEnd), around 'up'.
        float arcLen = Mathf.Abs(SweepEnd - SweepStart);
        if (arcLen < 0.5f) return;

        Vector3 startDir = Quaternion.AngleAxis(SweepStart, up) * fwd;
        UnityEditor.Handles.color = SweepArcColor;
        UnityEditor.Handles.DrawWireArc(pos, up, startDir, SweepEnd - SweepStart, radius * 0.4f);
    }

    private void DrawRingPreview(Vector3 center, float radius, Color color)
    {
        float halfThick = RingThickness * 0.5f;

        Gizmos.color = color;
        Gizmos.DrawWireSphere(center, radius + halfThick);

        Gizmos.color = new Color(color.r, color.g, color.b, color.a * 0.5f);
        Gizmos.DrawWireSphere(center, Mathf.Max(0f, radius - halfThick));
    }

    private static void DrawCapsulePreview(Vector3 a, Vector3 b, float halfWidth, Color color, Vector3 up)
    {
        Gizmos.color = color;

        // Two hemispheres (start/tip) and the side lines connecting them.
        Gizmos.DrawWireSphere(a, halfWidth);
        Gizmos.DrawWireSphere(b, halfWidth);

        Vector3 seg = b - a;
        if (seg.sqrMagnitude < 0.0001f) return; // tip on caster → the two hemispheres already show it

        Vector3 dir = seg.normalized;
        Vector3 perp = Vector3.Cross(dir, up).normalized;
        if (perp.sqrMagnitude < 0.001f)
            perp = Vector3.Cross(dir, Vector3.right).normalized;

        Gizmos.DrawLine(a + perp * halfWidth, b + perp * halfWidth);
        Gizmos.DrawLine(a - perp * halfWidth, b - perp * halfWidth);

        // Center segment (thin) so the capsule axis reads clearly.
        Gizmos.color = new Color(color.r, color.g, color.b, color.a * 0.4f);
        Gizmos.DrawLine(a, b);
    }
#endif
}
