# Module: Persistence

> Pre2 transition note (`0.4.0-pre.2`): this page documents the Persistence
> implementation retained from 0.3.9. Persistence V2 in Pre13 will replace its
> Context-provider snapshot path with a Durable projection of committed
> ContextFrame data. The concrete adapters and `MonoBehaviour` workflow below
> are not frozen 0.4 APIs.

CoCoFlow Persistence is a save/load module built around two durable data paths:

- **Legacy Context path**: 0.3.9 scene entity records, used to restore the retained Context-provider runtime.
- **Container path**: indexed gameplay data, used to store inventories, quests, facts, event states, rewards, and catalog-backed definitions.

The module does not allocate runtime gameplay IDs, does not drive frame-level gameplay, and does not serialize transient intent. Runtime behavior remains owned by Context providers, state machines, gameplay components, and container commands.

For the 0.4 target, Persistence may only capture a Durable projection derived
from a committed ContextFrame. It must not capture an IntentFrame, EventInbox,
CoCoEventAgent subscription, unpublished EventOutbox, intermediate Layer, Operator
Outcome, or partially processed Tick.

Pre2's ContextFrame projection Codec is an internal feasibility spike. It only
supports exact-layout, same-session data associated with the current
GraphInstanceId. It is not a save-document schema, migration contract, stable
wire identity, or supported cross-session load path. Persistence V2 must not
serialize this internal representation as a durable file format.

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

`CharacterContext` and `ItemContext` are optional types supplied by the
`Samples~/Gameplay` source handoff. Their reflection-based adapters remain in
the formal Persistence Runtime so the package compiles without importing the
sample and can recognize those Context types when a project includes them.

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

`contextSection` restores retained 0.3.9 scene-entity Context records. It is not
the 0.4 ContextFrame Durable projection schema.

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

## Legacy 0.3.9 Scene Setup

A minimal scene uses the following MonoBehaviours:

| GameObject | Component | Purpose |
|---|---|---|
| Scene runtime root | `PersistenceContainerStore` | Owns active container section and catalog reference. |
| Scene runtime root | project installer or bootstrap | Assigns a `PersistenceContainerCatalog` and materializes startup containers. |
| Persistent entity root | `PersistenceContext` | Provides stable scene identity and captures/applies Context. |
| Persistent entity root | `ICoCoContextProvider<TContext>` implementation | Owns the actual runtime Context. Optional `CharacterContextProvider` and `ItemContextProvider` implementations come from `Samples~/Gameplay`. |
| Entity or operation object | `PersistenceContainerBridge` | Publishes commands from legacy gameplay code. |

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
using CoCoFlow.Runtime.Modules.Persistence.Container;
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

### Legacy 0.3.9 Grant Reward Through Bridge

The following bridge call documents retained 0.3.9 code only. A 0.4 StateLogic
must produce a declared Persistence OperationFrame Section; a Persistence
Operator owns the concrete request. StateLogic must not resolve the bridge or
publish directly to the EventBus.

```csharp
var bridge = GetComponent<PersistenceContainerBridge>();
bridge.RequestGrantReward(
    "reward.world.gem_cache",
    PersistenceContainerStore.DefaultPlayerInventoryContainerId);
```

In the retained 0.3.9 implementation, `PersistenceContainerBridge` publishes a
`PersistenceContainerCommandRequested` event. The active
`PersistenceContainerStore` applies the command and publishes an applied or
rejected result.

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

## Boundaries

Persistence owns durable save contracts and file IO. It does not own:

- gameplay decision logic
- state-machine transition rules
- frame input or one-frame intent
- network authority
- runtime spawn reconstruction for arbitrary prefab clones
- partial-Tick or mid-Layer capture

For Persistence V2, Pre13 additionally owns:

- ContextFrame Durable projection encoding and schema migration
- a public, versioned save-document representation independent from the Pre2
  internal exact-layout Codec spike
- Container and world-fact integration
- StableEntityId resolution across load boundaries
- mapping a restored StableEntityId to the current GraphInstanceId before an
  Actor ContextFrame is reconstructed
- conversion of cross-save pending actions into Actor pending state or world facts

It explicitly does not persist EventInbox, IntentFrame, CoCoEventAgent subscriptions,
deduplication windows, or unpublished EventOutbox candidates.

Runtime-generated prefab clones can receive temporary IDs, but cross-save reconstruction of arbitrary spawned entities requires a future spawn contract using fields such as `prefabKey` and a spawn/container record.
