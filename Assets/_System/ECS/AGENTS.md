# ECS — Unity DOTS Layer (~209 files)

Core ECS architecture: Authoring → Baking → Components → Systems → Update Groups.

## Structure

```
ECS/
├── Authorings/        # Baker classes (MonoBehaviour → IComponentData)
├── Components/        # IComponentData / ISharedComponentData / IBufferElementData
├── Systems/           # ISystem (update) + SystemBase
└── Update Groups/     # Custom component system groups
```

## Authoring → Baking Flow

1. MonoBehaviour authoring components in `Authorings/` live on GameObjects.
2. Bakers (nested in each authoring) convert them to ECS `IComponentData`.
3. Resulting entities carry components from `Components/`.

## Systems

~~39 systems across subfolders:
- `Systems/Entity/Player/` — player movement, health, input handling
- `Systems/Entity/Enemy/` — enemy AI, spawning, behavior
- `Systems/Lobby/` — lobby/menu scene ECS logic
- `Systems/MetaProgression/` — persistent upgrade systems

## Anti-Patterns

- Do NOT put logic in Components. Components are data-only.
- Do NOT use `Entities.ForEach` with `WithStructuralChanges()` — use `EntityCommandBuffer`.
- Avoid `SystemBase` for new systems — prefer `ISystem` (unmanaged, burstable).
