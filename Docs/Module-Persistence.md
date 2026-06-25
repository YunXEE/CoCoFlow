# Module: Persistence

CoCoFlow Persistence is a save/load module built around two durable data paths:

- **Context path**: scene entity snapshots, used to restore state-machine driven runtime entities.
- **Container path**: indexed gameplay data, used to store inventories, quests, facts, event states, rewards, and catalog-backed definitions.

The module does not allocate runtime gameplay IDs, does not drive frame-level gameplay, and does not serialize transient intent. Runtime behavior remains owned by Context providers, state machines, gameplay components, and container commands.

## Runtime Structure

```text
PersistenceSaveLoadSystem
  -> PersistenceSession
     -> PersistenceContextRegistry
        -> PersistenceContext
           -> IPersistenceContextAdapter
           -> PersistenceContextSection
     -> PersistenceContainerStore
        -> PersistenceContainerSection
        -> PersistenceContainerCatalog
  -> PersistenceSaveDocument
     -> PersistenceFileStore
```

### Core

| Type | Responsibility |
|---|---|
| `PersistenceSaveLoadSystem` | Public static facade for manual save/load operations. |
| `PersistenceSaveDocument` | Versioned save document containing metadata, `contextSection`, and `containerSection`. |
| `PersistenceSaveSlotMetadata` | Slot index, display name, timestamps, and Unity version. |
| `PersistenceFileStore` | JSON file read/write and save-slot path handling. Writes through a temporary file before replacing the target file. |
| `PersistenceSession` | Captures current sections and keeps a pending loaded document for two-phase load behavior. |

### Context Path

| Type | Responsibility |
|---|---|
| `PersistenceContext` | The MonoBehaviour attached to a persistent scene entity. It exposes `StableEntityId`, registers itself, and captures/applies the local `ICoCoContextProvider<TContext>`. |
| `PersistenceContextRegistry` | Runtime registry keyed by `StableEntityId`. It captures all registered contexts and applies pending context records when entities register. |
| `PersistenceContextSection` | Save section containing `PersistenceContextRecord` entries. |
| `IPersistenceContextAdapter` | Adapter contract for translating specific Context types into durable records. |
| `PersistenceCharacterContextAdapter` | Captures/restores durable `CharacterContext` facts such as identity, lifecycle, semantic/action state, event sequence, motion, and health. |
| `PersistenceItemContextAdapter` | Captures/restores durable `ItemContext` facts such as identity, lifecycle, item state, and payload. |

`PersistenceContext` should be placed on scene entities that need state-machine restoration. It looks for a same-GameObject `ICoCoContextProvider<TContext>` and delegates capture/apply to registered adapters.

The Context path intentionally excludes:

- one-frame intent
- Unity object references such as runtime targets
- direct Transform references
- runtime-only requests such as warp commands

### Container Path

| Type | Responsibility |
|---|---|
| `PersistenceContainerStore` | Scene-level MonoBehaviour that owns runtime container state and processes container commands from the event bus. |
| `PersistenceContainerCatalog` | ScriptableObject catalog for static definitions: items, container contracts, templates, rewards, loot tables, events, facts, tags, and sequential quests. |
| `PersistenceContainerSection` | Save section containing runtime `PersistenceContainerRecord` entries. |
| `PersistenceContainerRecord` | Runtime state for one materialized container. |
| `PersistenceContainerSchemas` | Serializable schema types for definitions, entries, commands, quests, facts, events, rewards, tags, and loot tables. |
| `PersistenceContainerBridge` | MonoBehaviour helper that publishes container command events without directly mutating the store. |

Containers are indexed runtime records built from catalog definitions and templates. A container can represent an inventory, stash, chest contents, quest book, event log, or world fact set. Static definitions remain in the catalog; save files keep only runtime state.

## Save Document

The v1 save document contains exactly two gameplay sections:

```json
{
  "schemaVersion": 1,
  "metadata": {},
  "contextSection": {},
  "containerSection": {}
}
```

`contextSection` restores scene entity Context snapshots.

`containerSection` restores materialized container records such as inventory content, quest progress, event states, and world facts.

## Save Flow

```text
PersistenceSaveLoadSystem.SaveGame(slot)
  -> PersistenceSession.Capture(slot)
     -> PersistenceContextRegistry.CaptureSection()
        -> each PersistenceContext.TryCapture()
     -> PersistenceContainerStore.CaptureActiveSection()
  -> PersistenceSaveDocument.Create(...)
  -> PersistenceFileStore.WriteDocument(slot, document)
     -> write savegame_slot_N.json.tmp
     -> replace savegame_slot_N.json
```

Example:

```csharp
using CoCoFlow.Runtime.Modules.Persistence;

PersistenceSaveLoadSystem.CurrentSlotIndex = 0;
PersistenceSaveLoadSystem.SaveGame();
```

## Load Flow

```text
PersistenceSaveLoadSystem.LoadGame(slot)
  -> PersistenceFileStore.TryReadDocument(slot)
  -> PersistenceSaveDocument.MigrateToCurrentSchema(...)
  -> PersistenceSession.SetPendingDocument(document)
  -> PersistenceSession.ApplyPendingDocument()
     -> PersistenceContainerStore.ApplyActiveSection(...)
     -> PersistenceContextRegistry.ApplySection(...)

Later entity registration:
  PersistenceContext.OnEnable()
    -> PersistenceContextRegistry.Register(context)
    -> matching pending context record applies automatically
```

This allows two-phase loading: the file can be read before every scene entity has registered. Registered entities receive matching context records when they become available.

Example:

```csharp
using CoCoFlow.Runtime.Modules.Persistence;

bool loaded = PersistenceSaveLoadSystem.LoadGame(0);
```

## Scene Setup

A minimal scene uses the following MonoBehaviours:

| GameObject | Component | Purpose |
|---|---|---|
| Scene runtime root | `PersistenceContainerStore` | Owns active container section and catalog reference. |
| Scene runtime root | project installer or bootstrap | Assigns a `PersistenceContainerCatalog` and materializes startup containers. |
| Persistent entity root | `PersistenceContext` | Provides stable scene identity and captures/applies Context. |
| Persistent entity root | `ICoCoContextProvider<TContext>` implementation | Owns the actual runtime Context, such as `CharacterContextProvider` or `ItemContextProvider`. |
| Persistent entity root or child | `CoCoStateController` | Drives state restoration through the Context path. |
| Entity or operation object | `PersistenceContainerBridge` | Publishes container commands from gameplay code or states. |

For prefabs, `PersistenceContext.stableEntityId` should remain empty. Scene instances generate or receive stable IDs; prefab assets should not share a serialized scene-instance ID.

## Catalog Editing

Create a catalog asset from:

```text
Assets/Create/CoCoFlow/Persistence/Container Catalog
```

Open the catalog editor from:

```text
CoCoFlow/Persistence/Catalog Editor
```

The editor separates catalog data into tabs:

- Overview
- Items
- Containers
- Templates
- Rewards
- Loot
- Quests
- Events
- Facts
- Tags

Use `CoCoFlow/Persistence/Validate Selected Catalog` to validate duplicate IDs and missing references.

## Container Examples

### Materialize Startup Containers

```csharp
using CoCoFlow.Runtime.Modules.Persistence;
using UnityEngine;

public sealed class GameBootstrap : MonoBehaviour
{
    [SerializeField] private PersistenceContainerStore store;
    [SerializeField] private PersistenceContainerCatalog catalog;

    private void Awake()
    {
        store.SetCatalog(catalog);
        store.MaterializeStartupContainers();
    }
}
```

`materializeOnNewGame` templates can create initial records for player inventory, stash, quest book, event log, or world fact containers.

### Add Items

```csharp
store.AddItemToContainer(
    PersistenceContainerStore.DefaultPlayerInventoryContainerId,
    "item.medkit.basic",
    2);
```

### Transfer Items

```csharp
store.TransferItem(
    "container.player.inventory",
    "container.player.stash",
    "item.medkit.basic",
    1);
```

### Grant Reward Through Bridge

```csharp
var bridge = GetComponent<PersistenceContainerBridge>();
bridge.RequestGrantReward(
    "reward.chest_sample.gem_cache",
    PersistenceContainerStore.DefaultPlayerInventoryContainerId);
```

`PersistenceContainerBridge` publishes a `PersistenceContainerCommandRequested` event. The active `PersistenceContainerStore` applies the command and publishes an applied or rejected result.

### Sequential Quest Progress

V1 quests are linear. Branches, optional objectives, sub-quests, and full quest trees are not part of the first schema.

```csharp
store.ActivateQuest(
    PersistenceContainerStore.DefaultQuestBookContainerId,
    "quest.village.gem");

bridge.RequestEntityKilled(
    new[] { "Entity.Monster.GemGuardian" },
    PersistenceContainerStore.DefaultQuestBookContainerId);

bridge.RequestItemDelivered(
    "item.gem.red",
    new[] { "Entity.Npc.VillageElder" },
    PersistenceContainerStore.DefaultQuestBookContainerId);
```

Future complex quest trees should evolve as Container schemas, not as a third save section.

## Chest Sample

`Samples~/Chest Samples` demonstrates both persistence paths:

- `P_Chest_00.prefab`
  - `PersistenceContext` captures the chest `ItemContext`.
  - `ItemContextProvider`, `ItemLifeCycle`, and `ItemInputDriver` drive item state.
  - `CoCoStateController` restores the chest into Available, Opening, or Opened states.
  - `PersistenceContainerBridge` grants rewards and writes world facts/events.

- `ChestSample_Runtime.prefab`
  - `PersistenceContainerStore` owns runtime container state.
  - `ChestSampleSceneInstaller` creates a small runtime catalog and materializes startup containers.

The sample intentionally keeps prefab `stableEntityId` empty. Scene instances receive stable IDs when they are placed in a saved scene.

## Boundaries

Persistence owns durable save contracts and file IO. It does not own:

- gameplay decision logic
- state-machine transition rules
- frame input or one-frame intent
- network authority
- runtime spawn reconstruction for arbitrary prefab clones

Runtime-generated prefab clones can receive temporary IDs, but cross-save reconstruction of arbitrary spawned entities requires a future spawn contract using fields such as `prefabKey` and a spawn/container record.
