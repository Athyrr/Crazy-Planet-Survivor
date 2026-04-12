Two steps: wire up the scene, then opt enemies in.
                                                                                                                                                                                      
---                                                                                                                                                                                 
Step 1 — Add the FlowField singleton to your scene

1. In the scene that has your planet, create an empty GameObject → name it "FlowField"
2. Add the FlowFieldDataAuthoring component to it
3. Tune the inspector values:
   - Grid Width / Height: 64 (covers 80 units each side from player)
   - Cell Size: 2.5 — decrease for tighter obstacle routing, increase for performance
   - Rebuild Interval: 0.2 seconds

That's it for the scene. The singleton entity + buffer are baked automatically.

  ---
Step 2 — Opt an enemy prefab into flow field navigation

Open the enemy prefab authoring GameObject and make these two changes:

┌──────────────────────────────────────────────────────────────────┬─────────────────────────────────────────────────┐
│                              Remove                              │                       Add                       │
├──────────────────────────────────────────────────────────────────┼─────────────────────────────────────────────────┤
│ FollowMovementAuthoring (or whatever bakes FollowTargetMovement) │ FlowFieldFollowerMovement authoring (see below) │
├──────────────────────────────────────────────────────────────────┼─────────────────────────────────────────────────┤
│ —                                                                │ Keep HardSnappedMovement as-is                  │
└──────────────────────────────────────────────────────────────────┴─────────────────────────────────────────────────┘

Since FlowFieldFollowerMovement has no fields, the quickest authoring is a one-liner script you add to the enemy prefab:

// Assets/_System/ECS/Authorings/Movement/FlowFieldFollowerMovementAuthoring.cs
using Unity.Entities;
using UnityEngine;

public class FlowFieldFollowerMovementAuthoring : MonoBehaviour
{
private class Baker : Baker<FlowFieldFollowerMovementAuthoring>
{
public override void Bake(FlowFieldFollowerMovementAuthoring authoring)
{
var entity = GetEntity(TransformUsageFlags.Dynamic);
AddComponent<FlowFieldFollowerMovement>(entity);
}
}
}

Then on the enemy prefab: remove FollowMovementAuthoring, add FlowFieldFollowerMovementAuthoring.

  ---
Step 3 — Verify it works

Hit Play and watch:
- Enemies should still pathfind toward the player
- When they encounter an Obstacle entity, they should route around it instead of pushing through
- In the Entities window, the FlowFieldData singleton should show IsReady = true after the first 0.2s

  ---
Optional — visualize the field (editor debug)

Add this to any MonoBehaviour in the scene for a Gizmos overlay:

#if UNITY_EDITOR
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

public class FlowFieldDebugDrawer : MonoBehaviour
{
private void OnDrawGizmos()
{
if (!Application.isPlaying) return;

          var world = World.DefaultGameObjectInjectionWorld;
          if (world == null) return;

          var em = world.EntityManager;
          var query = em.CreateEntityQuery(typeof(FlowFieldData));
          if (query.IsEmpty) return;

          var entity = query.GetSingletonEntity();
          var data = em.GetComponentData<FlowFieldData>(entity);
          if (!data.IsReady) return;

          var cells = em.GetBuffer<FlowFieldCell>(entity);
          float arrowLen = data.CellSize * 0.4f;

          for (int i = 0; i < cells.Length; i++)
          {
              int cx = i % data.GridWidth;
              int cy = i / data.GridWidth;
              float lx = (cx - data.GridWidth / 2) * data.CellSize;
              float lz = (cy - data.GridHeight / 2) * data.CellSize;
              float3 worldPos = data.Origin + data.GridRight * lx + data.GridForward * lz;

              var cell = cells[i];
              if (cell.Cost == byte.MaxValue)
              {
                  Gizmos.color = Color.red;
                  Gizmos.DrawWireCube(worldPos, Vector3.one * data.CellSize * 0.4f);
              }
              else if (math.lengthsq(cell.Direction) > 0.001f)
              {
                  Gizmos.color = Color.Lerp(Color.green, Color.yellow, cell.Cost / 64f);
                  Gizmos.DrawRay(worldPos, cell.Direction * arrowLen);
              }
          }
      }
}
#endif
