# \_System — Game Code (361 C# files)

Entry point for all project-owned Unity code. Everything outside this folder is imported assets, plugins, or configuration.

## Structure

```
_System/
├── ECS/                  # DOTS code (Authorings, Components, Systems, Update Groups)
├── UI/                   # HUD, menus, shops, widgets (~35 files)
├── Settings/             # Game configuration & ScriptableObjects
├── Managers/             # Singleton-style game managers (transitional — prefer ECS)
├── Spells/               # Spell definitions & behaviour logic
├── Upgrades/             # Character upgrade definitions
├── Planets/              # Procedural planet generation
├── Save/                 # Save/load system
├── Inputs/               # Input handling
├── Audio/                # FMOD wrapper
├── Debug/Console/        # In-game debug console
└── _Core/                # Core utilities, SolarSystem root, shared helpers
```

## Where to Look

| Concern | Path |
|---------|------|
| ECS Components | `ECS/Components/` |
| ECS Systems | `ECS/Systems/` |
| ECS Authorings | `ECS/Authorings/` |
| UI (HUD & menus) | `UI/` |
| Spell system | `Spells/` + `ECS/Components/Spells/` |
| Progression | `Upgrades/`, `Metaprogression/`, `Amulets/` |

## Conventions

- DOTS-first: new logic goes into ECS Systems + Components, not MonoBehaviours.
- UI uses uGUI (Canvas-based) for HUD; some UI Toolkit in menus.
- Settings are ScriptableObject singletons stored in `_Datas/`.
