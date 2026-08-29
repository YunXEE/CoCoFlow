# Module: Persistence

> **Maturity: Mature** · Documentation baseline: `0.4.0` · Updated 2026-08-29
>
> The module originated in 0.3.9. The current Runtime API is stable and supports
> schema v2, Containers, and StateGraph ContextFrame persistence. The older
> `ICoCoContext` adapter path remains supported for compatibility.

Persistence writes local JSON save slots. A save document contains slot
metadata, per-entity Context records, and the active Container section.

```text
PersistenceSession.Capture(slot)
        ├─ PersistenceContextRegistry.CaptureSection()
        └─ PersistenceContainerStore.CaptureActiveSection()
                         ↓
PersistenceSaveDocument (schema v2)
                         ↓
temporary JSON → replace target → recovery backup on replacement failure
```

## Schema v2

`PersistenceSaveDocument.CurrentSchemaVersion` is `2`. The document contains:

| Field | Meaning |
|---|---|
| `schemaVersion` | Save schema version. Versions newer than the package supports are rejected. |
| `metadata` | Slot index and save metadata/timestamps. |
| `contextSection` | Records keyed by stable entity ID. |
| `containerSection` | Inventory, quest, event-log, fact, reward, and related Container records. |

Reading an older document runs `MigrateToCurrentSchema`. The supported v1 to v2
normalization preserves existing metadata and Context records, creates a
missing Container section, and writes the current schema number in memory.
This is a schema migration, not a scene or prefab migration system.

## Save files

`PersistenceFileStore` writes `savegame_slot_<index>.json` under
`Application.persistentDataPath`, unless `SaveDirectoryOverride` is set. It
serializes with Newtonsoft.Json to a `.tmp` file and then replaces the target.
During replacement, a `.bak` file is used as a recovery path; if the main file
is missing on read, an available backup is accepted. Successful replacement
cleans the transient backup.

`PersistenceSaveLoadSystem` exposes the simple slot API:

```csharp
PersistenceSaveLoadSystem.SaveGame(0);
bool loaded = PersistenceSaveLoadSystem.LoadGame(0);
```

`MaxSaveSlots` defaults to `3`, and `CurrentSlotIndex` defaults to `0`. Invalid
slots and I/O failures are logged and rejected. The file format is local JSON;
the module does not provide encryption, cloud synchronization, conflict
resolution, or multi-process locking.

## Stable entity identity

Add `PersistenceContext` to every entity whose Context should be saved. It owns
a serialized `stableEntityId` and optional `prefabKey`.

- scene instances receive persistent Editor-authored IDs;
- runtime-only instances receive an `RT_...` ID when needed;
- the registry indexes live contexts by that stable ID;
- a later registration can consume a matching record from the pending document.

Copied prefabs and dynamically spawned objects still require the project to
ensure the intended identity policy. Persistence does not spawn missing scene
objects or resolve a prefab key by itself.

## StateGraph ContextFrame path

When `PersistenceContext` finds a `CoCoStateGraphHost` on the same GameObject,
StateGraph is the preferred capture path:

1. `TryCapturePersistencePayload` captures the committed ContextFrame payload.
2. The payload is stored in a record discriminated as
   `CoCoFlow.StateGraph.ContextFrame`.
3. Loading validates that discriminator, the payload, stable identity, and the
   Host lifecycle before applying it.
4. Running or Suspended Hosts apply immediately through
   `TryApplyPersistencePayload`.
5. Created or Stopped Hosts defer the apply until the Host becomes live.
6. Disposed Hosts, missing same-object Hosts, malformed payloads, and rejected
   Host diagnostics fail closed.

The pending-document apply token prevents the same StateGraph record from being
consumed twice by one live context during that load. This payload is durable
save data and is separate from the same-session Temporal ring representation.

## Two-phase load

`LoadGame` performs two logical phases:

1. Read, deserialize, migrate, and install the document as
   `PersistenceSession.PendingDocument`.
2. Apply Containers and all currently registered Context records.

Contexts that register later check the pending section and apply their matching
record. StateGraph records may additionally wait for a Created or Stopped Host
to enter a live lifecycle. Call `PersistenceSession.ClearPendingDocument()`
when the project no longer wants late registrants to consume the loaded save.

## Compatibility Context adapters

If no `CoCoStateGraphHost` is present, `PersistenceContext` tries the registered
`IPersistenceContextAdapter` implementations against an `ICoCoContext` on the
same object. The package registers Character and Item adapters, and projects may
register additional adapters through `PersistenceContextAdapterRegistry`.

This path remains supported. It captures selected typed facts into
`PersistenceContextRecord`; it is not treated as deprecated or as an API that
must be replaced.

## Containers

`PersistenceContainerStore` owns the active `PersistenceContainerSection` and
an optional `PersistenceContainerCatalog`. Its current APIs cover materialized
templates, item stacks and transfers, quests/objectives, rewards, event states,
and facts. `PersistenceContainerBridge` publishes typed Container commands
through the CoCoFlow event boundary instead of directly mutating a remote
actor.

Container data is captured and restored with the same save document. Catalogs
define authored content; the serialized section holds the materialized runtime
records.

## Boundaries

- Saves capture logical Context and Container data, not a complete Unity scene
  or arbitrary component graph.
- A valid stable ID policy is required for entities that must survive reload.
- Save/load calls are synchronous at the file API boundary.
- Backup handling protects file replacement; it is not a versioned backup
  history.
- “Mature” means the public Runtime API and schema-v2 responsibilities are
  stable and usable. It does not claim zero defects, optimal serialization
  throughput, or store-grade save infrastructure.
