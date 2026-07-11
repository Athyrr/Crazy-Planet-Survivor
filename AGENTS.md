# ECS-Planet-Survivor

**Commit:** 891ba4a6 | **Branch:** master
**Stack:** Unity DOTS (ECS 1.3) + URP + FMOD + PrimeTween

## Overview

Top-down planet-survivor game built on Unity ECS (DOTS). Players survive waves of enemies on destructible/terraformable planets. Heavy use of Unity Physics, procedural VFX (VFX Graph), and UI Toolkit.

## Project Structure

```
Assets/
├── _System/           # ~360 C# files — game code (ECS + UI + tools)
├── _Scenes/           # Game scenes (SC_Main, SC_Earth, SC_Ice)
├── _Content/          # Art assets (VFX, textures, animations)
├── _Datas/            # ScriptableObject databases
├── _Prefabs/          # Game prefabs
├── Imported/          # Asset store content (MonsterForEnemyBundle, Piloto Studio)
└── Plugins/           # FMOD, PrimeTween
```

## Where to Look

| Task | Location | Notes |
|------|----------|-------|
| ECS Components | `Assets/_System/ECS/Components/` | All IComponentData types |
| ECS Systems | `Assets/_System/ECS/Systems/` | All ISystem/SystemBase types |
| ECS Authorings | `Assets/_System/ECS/Authorings/` | Baker/authoring components |
| UI (HUD, menus) | `Assets/_System/UI/` | Unity UI (uGUI) + UI Toolkit |
| Player logic | `Assets/_System/ECS/Systems/Entity/Player/` | Player-specific systems |
| Enemy logic | `Assets/_System/ECS/Systems/Entity/Enemy/` | Enemy spawning & AI |
| Spells & attacks | `Assets/_System/Spells/` + `_System/ECS/Components/Spells/` | Spell definitions & behaviors |
| Planets/terrain | `Assets/_System/Planets/` | Procedural planet generation |
| Save system | `Assets/_System/Save/` | Save/load game state |
| Settings | `Assets/_System/Settings/` | Game configuration |
| Debug console | `Assets/_System/Debug/Console/` | In-game debug tools |

## Conventions

- ECS patterns: IComponentData for data, ISystem/SystemBase for logic, Baker for authoring.
- Namespaces: Typically `ECS_Planet_Survivor.*` (verify per file).
- MonoBehaviours kept minimal — DOTS-first architecture.
- `_` prefix on folders = project-owned (vs Imported/ assets).

## Build

Unity Editor — open `SC_Main` scene and hit Play. No external build scripts detected.

## Notes

- Uses Unity Physics (not Havok). Custom Physics Authoring package for joint/body setup.
- VFX Graph for spell/explosion effects.
- FMOD for audio (bank files in `_AudioBanks/`).
- Game uses ScriptableObject databases under `_Datas/` for balance tuning.
