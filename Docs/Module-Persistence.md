# Module: Persistence

> **Maturity: Mature** · Documentation baseline: `0.4.4` · Updated 2026-10-18
>
> The module originated in 0.3.9. The current Runtime API is stable and supports
> schema v2, typed data Containers, and StateGraph ContextFrame persistence. The older
> `ICoCoContext` adapter path remains supported for compatibility.

Persistence writes local JSON save slots. A save document contains slot
metadata, per-entity Context records, and the active Container section.

```text
PersistenceSession.Capture(slot)
        ├─ PersistenceContextRegistry.CaptureSection()
        └─ PersistenceContainerHost.CaptureActiveSection()
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
| `containerSection` | Versioned business-struct records keyed by data type and ID. |

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

`PersistenceContainerHost` owns one table per business struct. A row is keyed
by **data type + ID**. Matching types and IDs share the same row; different types
may use the same ID. The module stores data; gameplay remains in business scripts.

The scene arrangement is:

```text
Global persistent object
├─ PersistenceHost                    optional component save/load entry
└─ PersistenceContainerHost           authoritative Container tables
   └─ ChestData["chest-01"]

Chest GameObject
├─ PersistenceContainer               locked ChestData type + chest-01 ID
└─ Chest                              same-object business script, holds a handle
```

The module has four runtime Mono types: `PersistenceHost`,
`PersistenceContainerHost`, `PersistenceContext`, and `PersistenceContainer`.
Existing Graph-only projects keep their current components and static API.
They do not need any new Mono. A scene-facing `PersistenceHost` delegates its
`SaveGame(int slotIndex = -1)` and `LoadGame(int slotIndex = -1)` calls to the
same existing static flow, slots, and Session.

### Host Inspector configuration

`PersistenceHost` stores the scene/prefab configuration: **Container Host**,
**Max Save Slots** (default 3), **Initial Slot** (default 0), and **Save Directory**.
The directory may be empty for `Application.persistentDataPath`, a relative
folder under that location, or an absolute path. Its Inspector shows the resolved
absolute directory and slot-file path, with **Open Folder** and **Copy Path**.
Previewing paths does not create directories. Configuration fields are locked
during Play; runtime values and the connected ContainerHost remain visible.

Enable **Debug Mode** before Play to save and load under the host project's
`Assets/DebugSaves/`. The Inspector previews this directory and disables the
retained **Save Directory** setting. Saving refreshes the Project window so the
JSON files can be inspected directly. Enabling or previewing the mode does not
create save files, move existing saves, or trigger automatic saving. Disable it
to use **Save Directory** again. Debug Mode only affects the Unity Editor;
Player builds always use the normal directory configuration.

Enable one global PersistenceHost before business scripts. It applies its saved
configuration to the shared static API; component and static save/load calls use
the same settings. Disabling it restores the previous static configuration.
The optional dragged ContainerHost must be the enabled global ContainerHost;
leaving it empty retains global registration, including Graph-only scenes.
Without a PersistenceHost, existing static configuration and Graph assembly stay
supported. The Save Editor reads Host-owned slot limits while a Host is active.

`PersistenceContainerHost` shows its active status, bound component count, table
count, total rows, and per-type row counts. It owns data tables; save paths and
slot configuration belong to PersistenceHost.

### Declare data and configure a Container

Root and custom nested structs require both `[Serializable, PersistenceData]`.
Public writable instance fields are stored. Properties, static, readonly and
`[NonSerialized]` fields are not stored. Private `[SerializeField]` data fields
are not supported on these business structs.

Supported values are booleans, ordinary integer types, float/double, char,
strings, enums, marked structs, and one-dimensional arrays or `List<T>` of
supported leaf/struct elements. Null strings and collections stay distinct
from empty values. Classes, Unity object references, delegates, pointers,
Dictionary, multidimensional/jagged arrays and collections of collections are
not supported. Nested structs belong to the root row; they are not separate
Containers or links to another Container.

Add one `PersistenceContainer` to the business GameObject. In its Inspector,
select the data type, enter a nonempty ID and edit the initial fields. Types
are explicitly locked; a generic API call does not choose or change the type.
The component serializes its type ID and initial JSON while the Inspector
draws the actual fields, including nested structs and collections. Undo and
prefab overrides use normal serialized properties.

```csharp
using System;
using CoCoFlow.Runtime.Modules.Persistence;
using CoCoFlow.Runtime.Modules.Persistence.Container;
using UnityEngine;

[Serializable, PersistenceData]
public struct ItemStackData
{
    public string Key;
    public int Count;
}

[Serializable, PersistenceData]
public struct ChestData
{
    public int Coins;
    public ItemStackData[] Items;
}

[RequireComponent(typeof(PersistenceContainer))]
public sealed class Chest : MonoBehaviour
{
    private PersistenceContainer _container;
    private PersistenceContainerHandle<ChestData> _data;

    private void Awake()
    {
        _container = GetComponent<PersistenceContainer>();
        _container.SetInitialData(new ChestData
        {
            Coins = UnityEngine.Random.Range(1, 6),
            Items = Array.Empty<ItemStackData>()
        });
    }

    private void Start()
    {
        _data = _container.GetHandle<ChestData>();
    }

    public int TakeCoins(int requested)
    {
        if (requested <= 0)
            return 0;

        ref ChestData data = ref _data.Value;
        int taken = Mathf.Min(requested, data.Coins);
        data.Coins -= taken;
        return taken;
    }
}
```

The Inspector can supply initial data instead of `SetInitialData`. Existing
rows, including loaded saves, always win over a new candidate. Randomness and
coin removal in the example belong entirely to `Chest`.

### Initialization and lifetime

Bootstrap the single ContainerHost on the first global persistent object,
before any business Container can enable. The framework does not create a
Host, wait, retry or choose a fallback. Its default execution order is -10000;
the bootstrap must still establish the global lifetime.

`PersistenceContainer` uses execution order 10000. Business initialization
must run earlier: business `Awake` prepares ID/initial data, Container
`OnEnable` registers, and business `Start` gets a handle. Project Settings
execution-order overrides must retain that relative order. Do not first get
the handle from an ordinary business `OnEnable`.

OnDisable unbinds the component but retains the Host row. Re-enabling binds
the existing row without resetting it or rerunning Awake/Start. Two enabled
Containers with matching type and ID share the row; later initial values are
ignored. Initial arrays/lists are copied, so changing a candidate does not
mutate the live row.

### API and references

`PersistenceContainer` exposes `Id`, `DataType`, `IsBound`,
`SetId(string)`, `SetInitialData<T>(in T)`, `GetHandle<T>()`, and
`TryGetHandle<T>(out PersistenceContainerHandle<T>)` (`T : struct`).
Configuration setters require an unbound component. Illegal types, mismatched
T and empty IDs are argument errors; requesting a handle before binding is
an InvalidOperationException. TryGetHandle returns false for an unbound
component, while invalid types remain errors.

A handle has `Id`, `IsValid`, and `ref T Value`. It resolves the Host's current
row on each access. Use `ref` to modify scalar fields:

```csharp
int coins = handle.Value.Coins;
ref ChestData data = ref handle.Value;
data.Coins += 1;
```

`var copy = handle.Value` copies the struct; scalar changes to that copy do not
write back. Arrays/lists retain ordinary C# shallow-reference semantics. Modify
struct elements of a List by reading, changing and assigning them back.
Keep a ref only for one synchronous operation, never across load/await/yield.
Keep handles rather than refs or collection references across loads.

Disabling a component does not invalidate a cached row handle. Changing its ID
while disabled does not retarget the old handle or move/delete the old row;
get a new handle after the new registration. A default handle, a destroyed Host,
or an absent row makes IsValid false; Value throws instead of creating data.

Pure-code access can explicitly reference the ready ContainerHost and call
`GetOrCreate<T>(string id, in T initialData)` or
`TryGet<T>(string id, out PersistenceContainerHandle<T> handle)`.
GetOrCreate only adopts the initial value when the row is missing. TryGet
does not create a row. Writes need no Commit, MarkDirty or event command.

### Saving and loading tables

The overall save remains schema v2. Only `containerSection` adds its own
`formatVersion: 1`, with records `{ typeId, id, data }`. Type IDs use
`AssemblySimpleName:Namespace.TypeName`, without assembly versions. Renaming
a business type/assembly has no automatic data migration. Missing business
fields use default values; obsolete fields are ignored.

Loading prepares validated tables before replacing the live tables. A cached
handle reads the restored row on its next access. A missing key with an active
Container is recreated from that component's candidate, in binding-registration
order; Awake and gameplay initialization are not rerun. Missing keys without
active bindings are removed, so old handles become invalid. Enabling such a
Container later follows normal registration.

Old Graph saves with absent, null or empty Container sections still load.
Nonempty legacy Container gameplay data is discarded with a warning; Graph
data is preserved. Unknown new versions/types, duplicate composite keys and
malformed data are rejected. File replacement, Graph payloads, pending tokens
and Graph apply timing use the existing flow.

Player builds include Container-specific linker metadata for marked Player
types and their field dependencies. Verify the actual target backend and
stripping settings with a Player save/load round trip; Editor tests do not
replace that check.

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
