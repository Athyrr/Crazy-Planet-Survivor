# UI — HUD & Screens

**35 .cs files** — UI Toolkit-based interfaces. All player-facing screens.

## WHERE TO LOOK
| Screen | Path |
|--------|------|
| HUD (health, timer, spells) | `HUD/` |
| Upgrade selection | `Upgrades/` |
| Character shop | `Characters Shop/` |
| Amulet shop | `Amulets Shop/` |
| Game over screen | `GameOver/` |
| Layout helpers | `Layout Group/` |
| Tab system | `Tab/` |
| Stat displays | `Attribute/` |

## CONVENTIONS
- UI Toolkit (UXML + USS), not uGUI/Canvas.
- `UIDocument` components attached to scene GameObjects.
- ECS components in `Components/UI/` for data-binding between ECS world and UI toolkit.
- No MonoBehaviour UI controllers — UI state driven by ECS component reads.
