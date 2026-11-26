#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Physics;
using Unity.Transforms;
using UnityEngine;
using Material = UnityEngine.Material;

/// <summary>Editor-only debug view of area attacks, toggled by the console command <c>debug_areas on|off</c> (off by
/// default, off again at every Play). Drawn as geometry (a line mesh and translucent spheres, on top of the scene)
/// through Graphics.RenderMesh, so it shows in the Game view and the Scene view whatever the Gizmos toggles — see
/// the Drawing section for why it is not Gizmos.
///   • Live zones: every AreaAttack at its real position and current state, computed like AreaAttackSystem does
///     (same pass/sweep helpers). Grey = delayed (before ActivationDelay), orange = pass 1, magenta = pass 2+,
///     green = OverTime aura. A cone shows its tangent-plane fan (edges + arc), its 3D aperture (the hit test is
///     dot(axis, toHit) ≥ cos(HalfAngle), a 3D cone whose axis lies in the plane normal to the entity's up), the up
///     axis used for the sweep, and an arrow in the sweep direction.
///   • Impact pulses: a sphere around each target a Burst zone registers a hit on (a new non-pinned entry in its
///     HitEntityMemory, diffed per frame and per pass), fading out over <see cref="PulseDuration"/>.
/// Read-only: AreaAttackSystem is untouched and nothing here feeds gameplay.</summary>
public sealed class AreaAttackDebugDrawer : MonoBehaviour
{
    public const float PulseDuration = 0.3f;
    public const float FallbackPulseRadius = 0.6f;
    private const int ArcSegments = 24;
    private const int MaxRecentHits = 64;

    private static readonly Color DelayedColor = new Color(0.65f, 0.65f, 0.65f, 0.7f);
    private static readonly Color FirstPassColor = new Color(1f, 0.45f, 0.05f, 1f);
    private static readonly Color LaterPassColor = new Color(1f, 0.2f, 0.85f, 1f);
    private static readonly Color OverTimeColor = new Color(0.2f, 1f, 0.45f, 1f);
    private static readonly Color UpAxisColor = new Color(1f, 1f, 1f, 0.6f);
    private static readonly Color PulseColor = new Color(1f, 0.92f, 0.1f, 1f);

    private enum EZoneState : byte { Delayed, Active, OverTime }

    private struct ZoneDraw
    {
        public EAttackAreaShape Shape;
        public EZoneState State;
        public int Pass;
        public float3 Position;   // hitbox centre (Offset applied), cone apex
        public float3 Up;         // entity up = surface normal at cast time (sweep axis)
        public float3 Forward;    // entity forward
        public float Radius;
        public float HalfAngle;   // radians
        public float Sweep;       // radians, current
        public float SweepDirection; // sign of the sweep progression this pass (0 = no sweep)
        public float RingThickness;
        public float3 CapsuleA, CapsuleB;
    }

    private struct Pulse
    {
        public Entity Target;
        public float3 Center;
        public float3 LocalCenter; // collider AABB centre in the target's space
        public float Radius;
        public float StartTime;
    }

    /// <summary>One registered hit, kept for measurement from the bridge (time, pass, zone apex and up, target centre).</summary>
    public struct HitRecord
    {
        public double WorldTime;
        public Entity Zone, Target;
        public int Pass;
        public float3 Apex, Up, Forward, TargetCenter;
        public float TargetRadius;
    }

    private sealed class ZoneTrack
    {
        public int Pass;
        public readonly List<Entity> Seen = new List<Entity>(8);
        public int LastFrame;
    }

    private static AreaAttackDebugDrawer _instance;
    private static readonly List<HitRecord> _recentHits = new List<HitRecord>(MaxRecentHits);

    public static bool Enabled => _instance != null;
    public static int TotalPulses { get; private set; }
    public static IReadOnlyList<HitRecord> RecentHits => _recentHits;

    private readonly List<ZoneDraw> _zones = new List<ZoneDraw>(32);
    private readonly List<Pulse> _pulses = new List<Pulse>(64);
    private readonly Dictionary<Entity, ZoneTrack> _tracks = new Dictionary<Entity, ZoneTrack>();
    private World _world;
    private EntityQuery _zoneQuery;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
        _recentHits.Clear();
        TotalPulses = 0;
    }

    public static void SetEnabled(bool on)
    {
        if (on == Enabled)
            return;
        if (on)
        {
            var go = new GameObject("[AreaAttackDebugDrawer]") { hideFlags = HideFlags.DontSave };
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<AreaAttackDebugDrawer>();
        }
        else
        {
            Destroy(_instance.gameObject);
            _instance = null;
        }
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    // ── Per-frame capture and draw submission ──

    private void LateUpdate()
    {
        _zones.Clear();
        float now = Time.unscaledTime;
        var world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
        {
            _pulses.Clear();
            _tracks.Clear();
            return;
        }

        var em = world.EntityManager;
        if (_world != world)
        {
            _world = world;
            _tracks.Clear();
            _pulses.Clear();
            _zoneQuery = em.CreateEntityQuery(ComponentType.ReadOnly<AreaAttack>(), ComponentType.ReadOnly<LocalToWorld>());
        }

        // AreaAttackSystem writes these from a scheduled job: complete it before reading on the main thread.
        em.CompleteDependencyBeforeRO<AreaAttack>();
        em.CompleteDependencyBeforeRO<LocalToWorld>();
        em.CompleteDependencyBeforeRO<HitEntityMemory>();
        em.CompleteDependencyBeforeRO<PhysicsCollider>();

        int frame = Time.frameCount;
        double worldTime = world.Time.ElapsedTime;
        using (var entities = _zoneQuery.ToEntityArray(Allocator.Temp))
        {
            for (int i = 0; i < entities.Length; i++)
            {
                Entity zone = entities[i];
                var area = em.GetComponentData<AreaAttack>(zone);
                var ltw = em.GetComponentData<LocalToWorld>(zone);

                if (TryBuildZone(em, zone, in area, in ltw, out var draw))
                    _zones.Add(draw);

                if (area.Cadence == EZoneCadence.Burst && em.HasBuffer<HitEntityMemory>(zone))
                    DetectHits(em, zone, in area, in ltw, frame, now, worldTime);
            }
        }

        // Forget zones that are gone.
        if (_tracks.Count > 0)
        {
            var stale = ListPool.Get();
            foreach (var kv in _tracks)
                if (kv.Value.LastFrame != frame)
                    stale.Add(kv.Key);
            for (int i = 0; i < stale.Count; i++)
                _tracks.Remove(stale[i]);
            ListPool.Release(stale);
        }

        // Age pulses; follow their target while it lives.
        for (int i = _pulses.Count - 1; i >= 0; i--)
        {
            var p = _pulses[i];
            if (now - p.StartTime > PulseDuration)
            {
                _pulses.RemoveAt(i);
                continue;
            }
            if (em.Exists(p.Target) && em.HasComponent<LocalToWorld>(p.Target))
            {
                p.Center = math.transform(em.GetComponentData<LocalToWorld>(p.Target).Value, p.LocalCenter);
                _pulses[i] = p;
            }
        }

        RenderDebug();
    }

    private void DetectHits(EntityManager em, Entity zone, in AreaAttack area, in LocalToWorld ltw,
        int frame, float now, double worldTime)
    {
        if (!_tracks.TryGetValue(zone, out var track))
        {
            track = new ZoneTrack { Pass = area.CurrentPass };
            _tracks.Add(zone, track);
        }
        track.LastFrame = frame;

        // AreaAttackSystem clears the (unpinned) hit memory at each new pass: a target can come back.
        if (track.Pass != area.CurrentPass)
        {
            track.Pass = area.CurrentPass;
            track.Seen.Clear();
        }

        var memory = em.GetBuffer<HitEntityMemory>(zone, true);
        for (int m = 0; m < memory.Length; m++)
        {
            var entry = memory[m];
            if (entry.Pinned || entry.HitEntity == Entity.Null || track.Seen.Contains(entry.HitEntity))
                continue;
            track.Seen.Add(entry.HitEntity);
            if (!TryAddPulse(em, entry.HitEntity, now, out var pulse))
                continue; // target already destroyed this frame: nothing to place the sphere on

            if (_recentHits.Count >= MaxRecentHits)
                _recentHits.RemoveAt(0);
            _recentHits.Add(new HitRecord
            {
                WorldTime = worldTime, Zone = zone, Target = entry.HitEntity, Pass = area.CurrentPass,
                Apex = math.transform(ltw.Value, area.Offset), Up = math.mul(ltw.Rotation, math.up()),
                Forward = math.forward(ltw.Rotation), TargetCenter = pulse.Center, TargetRadius = pulse.Radius,
            });
        }
    }

    private bool TryAddPulse(EntityManager em, Entity target, float now, out Pulse pulse)
    {
        pulse = default;
        if (!em.Exists(target) || !em.HasComponent<LocalToWorld>(target))
            return false;

        var ltw = em.GetComponentData<LocalToWorld>(target);
        float3 local = float3.zero;
        float radius = FallbackPulseRadius;
        if (em.HasComponent<PhysicsCollider>(target))
        {
            var collider = em.GetComponentData<PhysicsCollider>(target);
            if (collider.IsValid)
            {
                // Collider is in body space; LocalToWorld carries the uniform scale.
                Aabb aabb = collider.Value.Value.CalculateAabb();
                float scale = math.length(ltw.Value.c0.xyz);
                local = aabb.Center;
                radius = math.max(0.25f, 0.5f * math.cmax(aabb.Extents) * scale);
            }
        }

        pulse = new Pulse
        {
            Target = target, Center = math.transform(ltw.Value, local), LocalCenter = local, Radius = radius, StartTime = now,
        };
        _pulses.Add(pulse);
        TotalPulses++;
        return true;
    }

    private bool TryBuildZone(EntityManager em, Entity zone, in AreaAttack area, in LocalToWorld ltw, out ZoneDraw draw)
    {
        draw = new ZoneDraw
        {
            Shape = area.Shape,
            Position = math.transform(ltw.Value, area.Offset),
            Up = math.mul(ltw.Rotation, math.up()),
            Forward = math.forward(ltw.Rotation),
            HalfAngle = area.HalfAngle,
            RingThickness = area.RingThickness,
        };

        if (area.Cadence == EZoneCadence.OverTime)
        {
            if (em.HasComponent<BeamActive>(zone) && !em.IsComponentEnabled<BeamActive>(zone))
                return false;
            draw.State = EZoneState.OverTime;
            draw.Radius = area.RadiusStart;
            draw.CapsuleA = ltw.Position;
            draw.CapsuleB = draw.Position;
            if (em.HasComponent<AreaSegment>(zone))
            {
                var segment = em.GetComponentData<AreaSegment>(zone);
                draw.CapsuleA = segment.A;
                draw.CapsuleB = segment.B;
            }
            return true;
        }

        // Burst: same timing as BurstZoneJob (ElapsedTime already advanced this frame).
        float t = 0f;
        int pass = 0;
        if (area.ElapsedTime < area.ActivationDelay)
            draw.State = EZoneState.Delayed;
        else if (AreaAttackSystem.TryGetBurstPass(area.ElapsedTime - area.ActivationDelay, area.ActiveDuration,
                     area.Passes, out pass, out t))
            draw.State = EZoneState.Active;
        else
            return false; // window over, the zone waits for its Lifetime

        draw.Pass = pass;
        draw.Radius = math.lerp(area.RadiusStart, area.RadiusEnd, EmitterMath.EaseRadiusT(area.RadiusEase, t));
        draw.Sweep = math.lerp(area.SweepStart, area.SweepEnd, AreaAttackSystem.BurstSweepT(t, pass, area.AlternateSweep));
        float span = area.SweepEnd - area.SweepStart;
        draw.SweepDirection = math.abs(span) < 1e-3f ? 0f
            : math.sign(span) * (area.AlternateSweep && (pass & 1) == 1 ? -1f : 1f);
        draw.CapsuleA = ltw.Position;
        draw.CapsuleB = math.lerp(ltw.Position, draw.Position, t);
        return true;
    }

    // ── Drawing ──
    // Not Gizmos/Debug.DrawLine: the game camera is a URP stack (Main Camera + overlay "Camera - UI"), and URP draws the
    // Game-view gizmos with the last camera of the stack — the UI overlay — so world gizmos never show over the game
    // (measured 2026-11-27). A line mesh + sphere meshes rendered on the Default layer go through the Main Camera
    // (and the Scene view) like any geometry, on top of the scene (ZTest Always).

    private readonly List<Vector3> _lineVertices = new List<Vector3>(4096);
    private readonly List<Color> _lineColors = new List<Color>(4096);
    private readonly List<int> _lineIndices = new List<int>(4096);
    private Mesh _lineMesh;
    private Mesh _sphereMesh;
    private Material _lineMaterial;
    private Material _fillMaterial;
    private MaterialPropertyBlock _fillProps;

    private void RenderDebug()
    {
        if (!EnsureResources())
            return;

        _lineVertices.Clear();
        _lineColors.Clear();
        _lineIndices.Clear();

        for (int i = 0; i < _zones.Count; i++)
            DrawZone(_zones[i]);

        float now = Time.unscaledTime;
        var fillParams = new RenderParams(_fillMaterial) { layer = 0, matProps = _fillProps };
        for (int i = 0; i < _pulses.Count; i++)
        {
            var p = _pulses[i];
            float k = math.saturate((now - p.StartTime) / PulseDuration);
            float r = p.Radius * (1f + 0.35f * k);
            var wire = new Color(PulseColor.r, PulseColor.g * (1f - 0.6f * k), PulseColor.b, 1f - k);
            DrawWireSphere(p.Center, r, wire);

            _fillProps.SetColor(ColorId, new Color(wire.r, wire.g, wire.b, 0.35f * (1f - k)));
            Graphics.RenderMesh(in fillParams, _sphereMesh, 0,
                Matrix4x4.TRS(p.Center, Quaternion.identity, Vector3.one * (2f * r))); // built-in sphere: diameter 1
        }

        if (_lineVertices.Count == 0)
            return;
        _lineMesh.Clear();
        _lineMesh.SetVertices(_lineVertices);
        _lineMesh.SetColors(_lineColors);
        _lineMesh.SetIndices(_lineIndices, MeshTopology.Lines, 0, false);
        _lineMesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f); // never culled
        Graphics.RenderMesh(new RenderParams(_lineMaterial) { layer = 0 }, _lineMesh, 0, Matrix4x4.identity);
    }

    private static readonly int ColorId = Shader.PropertyToID("_Color");

    private bool EnsureResources()
    {
        if (_lineMaterial != null)
            return true;
        var shader = Shader.Find("Hidden/Internal-Colored"); // vertex colour × _Color, alpha blended, no lighting
        if (shader == null)
            return false;

        _lineMaterial = NewMaterial(shader, "AreaAttackDebugLines");
        _fillMaterial = NewMaterial(shader, "AreaAttackDebugFill");
        _fillProps = new MaterialPropertyBlock();
        _lineMesh = new Mesh { name = "AreaAttackDebugLines", hideFlags = HideFlags.HideAndDontSave };
        _lineMesh.MarkDynamic();
        _lineMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        _sphereMesh = Resources.GetBuiltinResource<Mesh>("New-Sphere.fbx");
        return true;
    }

    private static Material NewMaterial(Shader shader, string name)
    {
        var material = new Material(shader) { name = name, hideFlags = HideFlags.HideAndDontSave };
        material.SetColor("_Color", Color.white);
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        material.SetInt("_ZWrite", 0);
        material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Overlay;
        return material;
    }

    private void OnDisable()
    {
        if (_lineMesh != null) Destroy(_lineMesh);
        if (_lineMaterial != null) Destroy(_lineMaterial);
        if (_fillMaterial != null) Destroy(_fillMaterial);
        _lineMesh = null;
        _lineMaterial = _fillMaterial = null;
    }

    private void Line(float3 a, float3 b, Color color)
    {
        int index = _lineVertices.Count;
        _lineVertices.Add(a);
        _lineVertices.Add(b);
        _lineColors.Add(color);
        _lineColors.Add(color);
        _lineIndices.Add(index);
        _lineIndices.Add(index + 1);
    }

    private void DrawZone(in ZoneDraw z)
    {
        Color color = z.State switch
        {
            EZoneState.Delayed => DelayedColor,
            EZoneState.OverTime => OverTimeColor,
            _ => z.Pass == 0 ? FirstPassColor : LaterPassColor,
        };

        switch (z.Shape)
        {
            case EAttackAreaShape.Circle:
                DrawWireSphere(z.Position, z.Radius, color);
                break;

            case EAttackAreaShape.Ring:
                DrawCircle(z.Position, z.Up, z.Radius + z.RingThickness * 0.5f, color);
                DrawCircle(z.Position, z.Up, math.max(0f, z.Radius - z.RingThickness * 0.5f), color);
                break;

            case EAttackAreaShape.Capsule:
                DrawCapsule(z.CapsuleA, z.CapsuleB, z.Radius, z.Up, color);
                break;

            case EAttackAreaShape.Cone:
                DrawCone(in z, color);
                break;
        }

        // Up axis (surface normal at cast) used to orient the zone.
        Line(z.Position, z.Position + z.Up * 1f, UpAxisColor);
    }

    private void DrawCone(in ZoneDraw z, Color color)
    {
        float3 axis = math.normalize(math.mul(quaternion.AxisAngle(z.Up, z.Sweep), z.Forward));
        float3 side = math.normalizesafe(math.cross(z.Up, axis));
        float3 left = math.mul(quaternion.AxisAngle(z.Up, -z.HalfAngle), axis);
        float3 right = math.mul(quaternion.AxisAngle(z.Up, z.HalfAngle), axis);
        float r = z.Radius;

        // Tangent-plane fan: the two edges, the outer arc and the axis.
        Line(z.Position, z.Position + left * r, color);
        Line(z.Position, z.Position + right * r, color);
        DrawArc(z.Position, z.Up, left, z.HalfAngle * 2f, r, color);
        Line(z.Position, z.Position + axis * r, color);

        // 3D aperture: the cone is symmetric around its axis, so it also opens up/down by HalfAngle.
        var faint = new Color(color.r, color.g, color.b, color.a * 0.45f);
        float3 upEdge = math.mul(quaternion.AxisAngle(side, -z.HalfAngle), axis);
        float3 downEdge = math.mul(quaternion.AxisAngle(side, z.HalfAngle), axis);
        Line(z.Position, z.Position + upEdge * r, faint);
        Line(z.Position, z.Position + downEdge * r, faint);
        DrawArc(z.Position, side, downEdge, -z.HalfAngle * 2f, r, faint);
        DrawCircle(z.Position + axis * (r * math.cos(z.HalfAngle)), axis, r * math.sin(z.HalfAngle), faint);

        // Sweep direction: an arrow tangent to the outer arc at the leading edge.
        if (z.SweepDirection != 0f && z.State == EZoneState.Active)
        {
            float3 lead = z.SweepDirection > 0f ? right : left;
            float3 tangent = math.normalizesafe(math.cross(z.Up, lead)) * z.SweepDirection;
            float3 from = z.Position + lead * r;
            float3 tip = from + tangent * (r * 0.3f);
            Line(from, tip, Color.white);
            DrawArrowHead(tip, tangent, z.Up, r * 0.12f, Color.white);
        }
    }

    private void DrawCapsule(float3 a, float3 b, float halfWidth, float3 up, Color color)
    {
        DrawWireSphere(a, halfWidth, color);
        DrawWireSphere(b, halfWidth, color);
        float3 seg = b - a;
        if (math.lengthsq(seg) < 1e-6f)
            return;
        float3 perp = math.normalizesafe(math.cross(math.normalize(seg), up)) * halfWidth;
        Line(a + perp, b + perp, color);
        Line(a - perp, b - perp, color);
        Line(a, b, color);
    }

    private void DrawWireSphere(float3 center, float radius, Color color)
    {
        DrawCircle(center, math.up(), radius, color);
        DrawCircle(center, math.right(), radius, color);
        DrawCircle(center, math.forward(), radius, color);
    }

    private void DrawArrowHead(float3 tip, float3 dir, float3 up, float size, Color color)
    {
        float3 back = -math.normalizesafe(dir);
        Line(tip, tip + math.mul(quaternion.AxisAngle(up, math.radians(30f)), back) * size, color);
        Line(tip, tip + math.mul(quaternion.AxisAngle(up, math.radians(-30f)), back) * size, color);
    }

    private void DrawArc(float3 center, float3 normal, float3 from, float angle, float radius, Color color)
    {
        float3 prev = center + from * radius;
        for (int i = 1; i <= ArcSegments; i++)
        {
            float3 dir = math.mul(quaternion.AxisAngle(normal, angle * i / ArcSegments), from);
            float3 next = center + dir * radius;
            Line(prev, next, color);
            prev = next;
        }
    }

    private void DrawCircle(float3 center, float3 normal, float radius, Color color)
    {
        if (radius <= 0f)
            return;
        float3 n = math.normalizesafe(normal, math.up());
        float3 from = math.normalizesafe(math.cross(n, math.abs(n.y) < 0.99f ? math.up() : math.right()));
        DrawArc(center, n, from, 2f * math.PI, radius, color);
    }

    private static class ListPool
    {
        private static readonly List<Entity> _list = new List<Entity>(16);
        public static List<Entity> Get() { _list.Clear(); return _list; }
        public static void Release(List<Entity> list) => list.Clear();
    }
}

/// <summary>Console toggle of <see cref="AreaAttackDebugDrawer"/> (editor only).</summary>
public static class AreaAttackDebugCommands
{
    [ConsoleCommand("debug_areas", "Editor: draw area-attack zones and a fading sphere on each hit (Game and Scene views). on|off")]
    private static string DebugAreas(string mode = "")
    {
        bool on;
        if (string.Equals(mode, "on", System.StringComparison.OrdinalIgnoreCase)) on = true;
        else if (string.Equals(mode, "off", System.StringComparison.OrdinalIgnoreCase)) on = false;
        else return $"Usage : debug_areas on|off (currently {(AreaAttackDebugDrawer.Enabled ? "on" : "off")}).";

        if (on && !Application.isPlaying)
            return "debug_areas: Play mode only.";
        AreaAttackDebugDrawer.SetEnabled(on);
        return on
            ? "debug_areas on: zones (grey delayed, orange pass 1, magenta pass 2+, green OverTime) and hit pulses (Game and Scene views)."
            : "debug_areas off.";
    }
}
#endif
