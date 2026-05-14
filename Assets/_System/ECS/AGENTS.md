# ECS — Core Architecture

**190 .cs files** — All Entity Component System code. Every gameplay mechanic starts here.

## STRUCTURE
```
ECS/
├── Components/   # IComponentData structs (data only)
├── Systems/      # ISystem partial structs (logic only)
└── Authorings/   # MonoBehaviour + nested Baker (converts editor data → ECS)
```

## WHERE TO LOOK
| Category | Path |
|----------|------|
| Player/Enemy components | `Components/Entity/Player/`, `Components/Entity/Enemy/` |
| Movement components | `Components/Movement/` |
| Spell/Ability components | `Components/Spells/` |
| Movement systems | `Systems/` (root level) |
| System groups | `Systems/Entity/Player/`, `Systems/Entity/Enemy/` |
| Spell systems | `Systems/Spells Systems/` |
| Authoring → Baking | `Authorings/` (mirrors component structure) |
| Collision | `Systems/CollisionSystem.cs` + Damage components |

## CONVENTIONS
- **ISystem structs** — not SystemBase. All systems are `partial struct : ISystem`.
- **BurstCompile** — `[BurstCompile]` on every `OnCreate`/`OnUpdate`. Systems without it are exceptions.
- **Enableable components** — `IComponentData, IEnableableComponent` for toggle-able features. Enable/disable via baker `SetComponentEnabled<T>`.
- **ComponentLookup** — cached in `OnCreate`, updated with `.Update(ref state)` in `OnUpdate`.
- **ECB** — Always `EndSimulationEntityCommandBufferSystem.Singleton` for structural changes (destroy, spawn). `EndInitializationEntityCommandBufferSystem` for startup.
- **Queries** — `SystemAPI.Query<RefRW<T>, RefRO<U>>()` for read-write. `.WithAll<T>()` / `.WithNone<T>()` for filtering.
- **Damage pipeline** — `DamageBufferElement` buffer → `HealthSystem` (applies resistances/armor) → `Destructible` flag → cleanup.
- **Spells** — Spawned via `SpellPrefab` entity + `CastSpellRequest`, tracked in `ActiveSpell` buffer on player.

## ANTI-PATTERNS
- No `Entities.ForEach` anywhere.
- No `GameObject.Find` or `Resources.Load` in systems.
- No managed `SystemBase` classes.

## SYSTEM ORDER
Systems update in this sequence (annotated where relevant):
1. `InitializationSystemGroup`: `PlayerInputSystem`, `RunInitializationSystem`
2. `SimulationSystemGroup`: `SpellCastingSystem` → `SpellStatsCalculationSystem` → `CollisionSystem` → `HealthSystem`
3. `FixedStepSimulationSystemGroup`: `CollisionSystem` (physics triggers)
