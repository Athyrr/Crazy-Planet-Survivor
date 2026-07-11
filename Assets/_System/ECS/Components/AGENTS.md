# ECS Components — Data Layer (114 files)

All `IComponentData`, `ISharedComponentData`, and `IBufferElementData` types.

## Subdirectories

| Path | Purpose |
|------|---------|
| `Movement/` | Velocity, steering, flowfield following |
| `Damage/` | Health, damage buffers, invincibility |
| `Spells/` | Active spells, child entities, spell behaviors |
| `Effects/` | Status effects (buffs, debuffs, DoTs) |
| `Stats/` | Attribute/stat components (strength, speed, etc.) |
| `Entity/Player/` | Player-specific tags & data |
| `Entity/Enemy/` | Enemy-specific tags & data |
| `Entity/Boss/` | Boss-specific tags & data |
| `Lifetime/` | Entity lifetime / auto-destroy timers |
| `Spawn/` | Spawner configuration, wave management |
| `FlowField/` | Flowfield pathfinding data |
| `Inputs/` | Input state components |
| `UI/` | UI-bound ECS data (health bars, score) |
| `Planet/` | Planet chunk data, terrain modification |
| `Resources/` | Resource drops, experience orbs |
| `Upgrades/` | Applied upgrade data |
| `Run Progression/` | Per-run state (time, difficulty scaling) |
| `Databases/` | Component wrappers around ScriptableObject assets |

## Rules

- **Data only** — no logic, no system references, no `Transform` caching.
- All components used by at least one `System` or `Baker`.
- Prefer `IComponentData` (unmanaged structs). Use `ISharedComponentData` for archetype chunking.
- Use `IBufferElementData` for dynamic lists (damage events, child entities).
