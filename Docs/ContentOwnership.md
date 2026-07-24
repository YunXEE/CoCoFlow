# Content Acquisition and Ownership

> Contract status: `0.4.0-pre.10` · Updated 2026-07-24
>
> Pre10 Map integration verification: `UNVERIFIED` until the Unity-host,
> package, Player-build, and Package Validation Suite evidence is recorded.

Pre8 adds one Unity-facing acquisition and ownership boundary for content whose
runtime lifetime must be explicit. It does not require every serialized Unity
reference to pass through Content. Small, fixed project references may remain
ordinary Direct References when no runtime release boundary is needed.

## Mental model

- `ContentReference` describes a stable `ContentId`, content kind, backend, and
  backend locator or Direct object.
- `ContentRuntime` owns in-flight loads and the backend resource record.
- Every successful request receives an independent reference-type
  `ContentLease`.
- A lease value is valid only while that lease is live. `Dispose` is idempotent,
  clears the lease's strong value reference, and removes it from its owning
  Scope immediately so a long-lived Scope cannot pin already released
  Addressables content in memory.
- `ContentScope` owns its pending requests and leases. Disposing one Scope does
  not cancel another Scope's waiter or release another Scope's lease.
- The final lease starts backend release immediately. There is no hidden grace
  period, LRU, or permanent content cache.

Content supports three kinds: Asset, Prefab Source, and Additive Scene. A
Prefab Source lease keeps the source available; it never owns an instantiated
GameObject. The Pre9 Pooling module consumes that source lease and separately
owns physical instances and rental generations.

## Runtime and backend boundary

`ContentRuntime` is created explicitly by a project/world composition root.
`CoCoContentHost` is the package MonoBehaviour for that role; it is not a
singleton, and consumers reference the intended Host explicitly. Runtime
creation must start on the Unity main thread; worker-thread factory calls fail
with a structured `ContentMainThreadRequired` diagnostic before backend
registration begins.

The Direct backend is always registered by the Host:

- Asset and Prefab Source return the serialized Unity Object immediately.
- Releasing a Direct asset lease never destroys the source object and does not
  promise native-memory reduction.
- Additive Scene uses `SceneManager`; the final scene lease unloads the exact
  scene instance owned by the backend. Direct additive Scene loads are
  serialized across Content Runtime instances because `SceneManager` does not
  return the loaded `Scene` handle from its asynchronous operation.
- Direct Scene locators are resolved against Build Settings before physical
  loading. Full paths, Build Settings-relative paths, bare names, optional
  `.unity` suffixes, slash variants, and case variants resolve to one canonical
  path; an ambiguous bare name selects the first Build Settings entry.
- Do not load the same Direct Scene path concurrently through both Content and
  an out-of-band `SceneManager` call. Unity does not expose an operation-to-Scene
  handle correlation API, so exact ownership is only guaranteed when Content is
  the sole authority for that path during its serialized load.

The Addressables backend is an optional conditional assembly. It keeps raw
Addressables handles private and presents the same request/result/lease model as
Direct. The package manifest does not force Addressables into Direct-only
projects; Setup Assistant provides an explicit optional installation action.
The supported package range is `[2.9.1,3.0.0)`.

## Concurrency and failure

An exact request key is the Content ID, kind, expected type, and registered
backend generation. Overlapping requests share one physical load. Cancellation
removes only that caller's waiter. If all waiters leave, Content requests
backend cancellation; a backend that completes late is released without
publishing an ownerless lease.

Load failure removes only the failed generation, so the next request may retry
when the backend acquired no cleanup authority. A backend that owns a handle
while reporting load failure returns `FailureWithCleanup`; Content executes that
cleanup exactly once before deciding whether retry is safe. Successful cleanup
removes the failed generation. Failed cleanup retains a diagnostic tombstone and
blocks a second generation for that key because the old backend ownership may
have been partially released. Pre8 deliberately has no automatic release retry.

All registry state transitions are serialized on the Unity main thread.
Expected cancellation and backend failures are represented by structured
results rather than leaking backend exceptions.

## Diagnostics

The runtime exposes immutable debug snapshots and a fixed-capacity event
ledger. These contain IDs, generations, owner/scope/request/lease sequences,
counts, states, and structured diagnostics. They never retain Unity assets,
Scenes, leases, Addressables handles, or exception objects.

Acquisition stack capture is enabled only by the selected runtime option. It is
normally available in Editor/Development builds and disabled in Release builds.

## Consumer boundaries

- UI owns panel instances and keeps one Prefab Source lease alive until each
  instance is actually destroyed. Pre9 does not migrate the retained UI module
  to Pooling; navigation and any later pooled-UI policy remain Pre12.
- Map resolves owner-scoped Region demand into transactional Region-global and
  per-Chunk participant nodes. Its built-in Content participant is the sole
  Additive Scene lease authority for Map-managed scenes. Public Map participant
  context does not expose raw `ContentScope`, so project and TA participants
  cannot acquire or release the managed Scene out of band.
- A managed Chunk Scene cold-starts with exactly one metadata-only
  `CoCoRegionChunkAnchor` root; all other managed roots begin inactive. Map
  resolves fragments only after Content returns the exact Scene lease and scans
  only that leased Scene. A Map Direct Scene locator must be a full asset path;
  an Addressables locator must resolve to one unique Scene asset at authoring
  validation time.
- Additive Scenes are never pooled. Map can bind a separate Pool Scope to a
  stable committed participant node for transient instances inside the Region,
  but that Scope cannot retain, release, or substitute the Scene lease.
- One prepared Pool Entry owns exactly one Prefab Source `ContentLease` until
  its idle, rented, pending-destroy, and Temporal-retained physical instances
  are terminal. Pooling owns those instances and their generation-safe
  `PooledHandle` values; it does not alter Content's backend release rules.
- Direct and optional Addressables Prefab Sources enter Pooling through the
  same Content request. Pooling never owns a raw Addressables handle.
- A Pool runtime registers an internal shutdown dependency with its
  `ContentRuntime`. Content shutdown begins and awaits Pool's physical-instance
  drain before disposing Content Scopes, so source release does not depend on
  Unity component destruction order.

Normal world shutdown is explicitly composed as Map, then Pool, then Content.
If Content shutdown begins first, it is treated as an idempotent terminal
fallback: dependent Map and Pool ownership drains coordinate before Content
disposes their Scopes. This fallback is not the normal lifecycle order and does
not make Content a global Map/Pool service locator.

Content leases, Unity Objects, backend locators, and handles never enter
StateFlow Frames, Temporal history, or Persistence documents.

See [Object Pooling and Instance Ownership](ObjectPooling.md) for the instance
ownership, reset, capacity, Scope-close, and Temporal-retention contracts. See
[Map Region Fidelity](Module-Map.md) for Region/Chunk demand, cold-start Scene,
and transactional participant ownership.
