# _System — Project Source Code

**282 .cs files** — All gameplay code lives here. Organized by architectural layer.

## WHERE TO LOOK
| Task | Path |
|------|------|
| ECS components, systems, authoring | `ECS/` |
| UI Toolkit screens | `UI/` |
| Spell definitions | `Spells/` |
| Debug tools (console, FPS, gizmos) | `Debug/` |
| Shared constants, layers, utilities | `_Core/` |
| Save/load | `Save/` |
| Settings (camera, planet, UI) | `Settings/` |
| Planet component definitions | `Planets/` |
| Upgrade definitions | `Upgrades/` |

## CONVENTIONS
- **Namespaces** mirror folder hierarchy from `_System.` root (e.g., `_System.ECS.Components.Movement`).
- Config ScriptableObjects in root `_System/` or `_System/Spells/` — not in ECS subdirs.
- **No test framework** — no test .asmdef exists in this project.
- Singleton ECS components for global state: `GameState`, `InputData`, `RunProgression`.
