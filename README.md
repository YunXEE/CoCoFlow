# CoCoFlow

CoCoFlow is a modular Unity framework for Context-driven gameplay, explicit State Layers, reusable gameplay components, persistence, editor tooling, and optional samples.

> **Version**: 0.3.6 · **Unity**: 6000+

## Package Scope

CoCoFlow provides a runtime foundation for gameplay code that is organized around explicit Context contracts and state-machine topology. The package focuses on reusable framework surfaces rather than complete game features.

The current package includes:

- Core services, event bus, Context contracts, and State Layer runtime.
- Character, Enemy, and Item gameplay foundations.
- Input, Camera, UI, Animation, Map, Rendering, and Persistence modules.
- Editor tooling for setup, state graph inspection, persistence save slots, and catalog editing.
- Optional samples for Player, Enemy, Chest, and Network integration planning.

## Runtime Topology

```text
CoCoFlow
│
├── Runtime
│   ├── Core
│   │   ├── CoCoServices
│   │   ├── CoCoEventBus
│   │   ├── ICoCoContext / ICoCoContextProvider<TContext>
│   │   ├── CoCoStateController / CoCoStateLayer / CoCoStateBase
│   │   └── CoCoStateDefinition
│   │
│   ├── Modules
│   │   ├── Input
│   │   ├── Camera
│   │   ├── UI
│   │   ├── Animation
│   │   ├── Map
│   │   ├── Rendering
│   │   └── Persistence
│   │
│   └── Gameplay
│       ├── Character
│       ├── Enemy
│       └── Item
│
└── Editor
    ├── Core
    ├── AssetPipeline
    ├── Modules
    └── Gameplay
```

## Core Concepts

| Concept | Description |
|---|---|
| Context | A durable, typed gameplay data contract exposed through `ICoCoContextProvider<TContext>`. |
| State Layer | A named state machine surface owned by one `CoCoStateController`. Multiple layers can update in explicit order. |
| State Definition | Metadata declared by states to describe Context reads/writes, operations, and transitions. |
| Event Bus | Typed event dispatch with optional event envelopes for cross-system communication. |
| Persistence Context | Scene entity snapshot path for restoring Context-backed state machines. |
| Persistence Container | Catalog-backed runtime data path for inventories, quests, events, facts, rewards, and tags. |

## Modules

| Module | Status | Summary |
|---|---|---|
| Core | Stable foundation | Service locator, event bus, Context contracts, State Layer controller, state definitions, and logging. |
| Input | Usable foundation | Input reader and input intent contracts. |
| Camera | Usable foundation | Third-person camera and Cinemachine-oriented camera helpers. |
| UI | Usable foundation | View/controller abstractions and panel stack management. |
| Animation | Utility layer | Animator helpers, animation event state machine behaviour, and editor injection tooling. |
| Map | Usable foundation | Map manager and chunk loading support. |
| Rendering | Utility layer | Rendering quality helpers. |
| Persistence | Active module | Versioned save documents, temporary-file JSON writes, Context snapshots, Container data, catalog editing, and save-slot editor tooling. |
| Gameplay.Character | Active foundation | Character context provider, input driver, lifecycle writer, locomotion, and navigation motor. |
| Gameplay.Enemy | Active foundation | Enemy brain, spline navigation source, vision query, and engagement zone. |
| Gameplay.Item | Active foundation | Item context, item context provider, input driver, and item lifecycle writer. |

## Persistence

Persistence stores two sections in each save document:

- `contextSection`: scene entity Context snapshots captured through `PersistenceContext`.
- `containerSection`: runtime container data captured from `PersistenceContainerStore`.

The module includes manual save/load entry points, save slot metadata, schema migration entry, temporary-file replacement, a catalog editor, and a command bridge for container operations.

See [Module-Persistence](Docs/Module-Persistence.md) for setup, data flow, and usage examples.

## Dependencies

| Package | Version | Required by |
|---|---:|---|
| Addressables | 2.9.1 | package runtime/editor workflows |
| Input System | 1.18.0 | Input module |
| Newtonsoft Json | 3.2.2 | Persistence |
| Cinemachine | 3.1.6 | Camera module |
| AI Navigation | 2.0.0 | Character navigation and Enemy samples |
| Mathematics | 1.3.3 | Enemy and spline-related workflows |
| Splines | 2.6.0 | Enemy spline support |

Optional third-party packages can be installed by a project when a sample or business module requires them. They are not bundled into the core runtime surface.

## Installation

Install the package through Unity Package Manager using a Git URL, or place the package in a Unity project's `Packages/` directory.

After installation:

1. Open `CoCoFlow/Setup/Setup Assistant`.
2. Review required package dependencies.
3. Install optional samples as needed.
4. Use `CoCoFlow/State/State Graph Viewer` to inspect a `CoCoStateController`.
5. Use `CoCoFlow/Persistence/Catalog Editor` to edit persistence catalog assets.

## Samples

| Sample | Import Path | Purpose |
|---|---|---|
| Player Samples | `Assets/CoCoFlow/Player` | Demonstrates a player prefab with `CharacterContextProvider`, locomotion, and explicit State Layers. |
| Enemy Samples | `Assets/CoCoFlow/Enemy` | Demonstrates enemy context, brain, spline navigation, state scripts, and prefab wiring. |
| Chest Samples | `Assets/CoCoFlow/Chest` | Demonstrates Persistence Context and Container paths with a chest prefab and runtime container store. |
| Network Samples | `Assets/CoCoFlow/Network` | Documents network adapter boundaries and includes a container event bridge sample without adding a network package dependency. |

Samples are integration references. They are not complete game templates.

## Editor Tools

| Menu | Purpose |
|---|---|
| `CoCoFlow/Setup/Setup Assistant` | Dependency status and optional sample setup. |
| `CoCoFlow/State/State Graph Viewer` | Read-only graph view for controllers, layers, states, Context usage, operations, and transitions. |
| `CoCoFlow/Persistence/Save Editor` | Manual save/load slot tooling for local testing. |
| `CoCoFlow/Persistence/Catalog Editor` | Tabbed editor for Persistence catalog definitions. |
| `CoCoFlow/Persistence/Validate Selected Catalog` | Catalog ID and reference validation. |

## Documentation

- [Context / Network Boundary](Docs/ContextNetworkBoundary.md)
- [Module: Persistence](Docs/Module-Persistence.md)
- [Network Context Sync Plan](Samples~/Network%20Samples/CoCoFlow/Network/Docs/ContextSyncPlan.md)

## License

MIT
