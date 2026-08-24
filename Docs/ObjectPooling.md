# Object Pooling and Instance Ownership

> Contract status: `0.4.0-rc.0` · Updated 2026-08-22
>
> Verification: dual-host package-wide EditMode/PlayMode (editor and player
> mode) matrix evidence recorded through the Pre15 line — see CHANGELOG
> `0.4.0-rc.0`; Package Validation Suite remains locally waived.

Pre9 adds a Unity-facing GameObject instance-ownership boundary on top of
Content. It reduces repeated `Instantiate`/`Destroy` work for high-frequency
transient objects without bypassing content acquisition or exposing a competing
generic pool API.

The public contract is CoCoFlow-specific. `UnityEngine.Pool.ObjectPool<T>` is a
private implementation detail and may be replaced without changing consumers.

## Three ownership layers

```text
ContentReference
  -> ContentLease<GameObject> owns one loaded Prefab Source
       -> PoolScope / Pool Entry owns physical GameObject instances
            -> PooledHandle grants one generation of consumer authority
```

- Content decides how a Prefab Source is acquired and when its backend ownership
  can be released.
- One prepared Pool Entry retains exactly one Prefab Source `ContentLease` while
  any idle, rented, or Temporal-retained physical instance still exists.
- A readonly `PooledHandle` identifies one Pool, Scope, physical-instance
  sequence, and rental generation. The raw GameObject is not return authority.
- Returning or transferring a generation invalidates all copied handles for
  that generation. Duplicate, stale, and cross-Scope returns are diagnostics,
  not permission to mutate the current rental.

Direct and optional Addressables sources use the same Content path. Pooling
never stores an Addressables handle and does not add Addressables to
`package.json`.

## Explicit composition

`CoCoPoolHost` references the intended `CoCoContentHost` explicitly. It creates
one `PoolRuntime`; the runtime creates owner-scoped `PoolScope` instances. There
is no global Pool singleton or implicit Host lookup.

One Scope may prepare multiple `PoolId` entries. It owns their asynchronous
preparation, source leases, rented generations, retained instances, diagnostics,
and final close.

The built-in Map Pool participant creates a Scope for one stable committed Map
plan node. The Scope is not owned by a transition generation: an unchanged node
reuses it, while a fingerprint change prepares a candidate Scope. After the new
node commits, Map closes the replaced Scope before releasing that node's Scene
lease. A Chunk-scoped Pool participant must directly depend on that Chunk's
owning Content slot, so the compiler can guarantee Pool-before-Content reverse
cleanup.

Pool operations are Unity-main-thread operations. The runtime rejects calls
from other threads with a structured diagnostic before touching Unity objects
or internal pool state.

## Profile, prepare, and prewarm

A serializable `PoolProfile` contains:

- one stable `PoolId`;
- one Prefab Source `ContentReference`;
- `PrewarmCount`;
- `MaxRetained`.

`PrewarmCount` is the desired prepared idle count. `MaxRetained` is the maximum
number of idle instances kept after returns. Both are non-negative, and
`PrewarmCount` cannot exceed `MaxRetained`.

Preparation is asynchronous:

```csharp
PoolPrepareResult result =
    await scope.PrepareAsync(profile, cancellationToken);
```

The first prepare acquires the Prefab Source through the Scope's Content
ownership, creates and validates the Entry, prewarms it, and publishes `Ready`
only after all required instances succeed. A cancelled or failed initial
prepare destroys partial instances and releases the unpublished source
ownership. An exact concurrent prepare is single-flight; cancellation removes
only that caller's waiter.

Preparing the same `PoolId` with a conflicting source or capacity is rejected.
Runtime profile mutation and hot capacity reconfiguration are not supported.

`PrewarmAsync(poolId, cancellationToken)` explicitly refills a Ready Entry
toward `PrewarmCount`. `TryClearInactive(poolId, ...)` explicitly destroys only
idle instances while keeping the Entry and source lease Ready. Clear does not
auto-prewarm again.

There is no automatic trim, LRU, grace period, memory-pressure policy, or hidden
background refill.

## Rent, bind, activate, and return

Rent is synchronous after an Entry is Ready:

```csharp
if (scope.TryRent(poolId, out PooledHandle handle, out CoCoDiagnostic diagnostic))
{
    if (handle.TryGetInstance(out GameObject instance, out diagnostic))
    {
        // Bind parent, transform, and domain data while the instance is inactive.
        handle.TryActivate(out diagnostic);
    }
}
```

`TryRent` returns an inactive instance. The consumer binds its data and transform
before `TryActivate` runs Pool callbacks and calls `SetActive(true)`. This
guarantees bind-before-activation; it does not promise that first `Awake` waits
for consumer binding.

Pool-aware components implement the synchronous `IPoolable` contract:

```csharp
bool TryOnRent(in PoolRentContext context, out CoCoDiagnostic diagnostic);
bool TryOnReturn(in PoolReturnContext context, out CoCoDiagnostic diagnostic);
```

Participants are discovered once when the physical instance is created.
Rent callbacks run in deterministic hierarchy/component order; Return callbacks
run in the exact reverse order.

`PooledHandle.TryReturn`, `PoolScope.TryReturn`, and `Dispose` all converge on
the same generation-safe return path:

1. consume the current rental generation;
2. deactivate the GameObject;
3. run reverse reset callbacks;
4. restore the retention parent/baseline transform;
5. retain the instance as idle or destroy it.

The baseline is the prefab root's local position, rotation, and scale captured
when the physical instance is created under the Retention Root. Every successful
normal Return and Temporal return to retention restores those root-local values.
Nested transforms and domain-specific state remain the responsibility of
`IPoolable`; Pooling does not infer a deep object reset.

If a Rent or Return callback refuses or throws, the runtime continues
best-effort cleanup and destroys that physical instance. It is never silently
reused with unknown state. External destruction is also detected, reconciled
against authoritative counts, and recorded as a diagnostic. When an external
`Destroy` overlaps a same-frame force shutdown, the event category is
best-effort according to the first observable owner. The strong guarantees are
one terminal record, invalidated generations, and source ownership retained
through the physical-destruction barrier.

## Capacity semantics

`MaxRetained` limits only idle retention. It is not `MaxActive` or `MaxTotal`.

- An empty Ready pool creates on demand.
- A burst may rent more instances than `PrewarmCount` or `MaxRetained`.
- Returning above `MaxRetained` destroys the overflow instance.
- `MaxRetained == 0` is valid and means every returned instance is destroyed.
- Returning overflow is still a successful return; it does not restore consumer
  authority.

Projects that need admission control, projectile budgets, spawn limits, or
back-pressure implement that policy in their domain layer before Rent. Pooling
does not decide gameplay capacity.

## Scope close and source release

Closing a Scope rejects new prepare, prewarm, and rent operations, cancels
pending preparation, and destroys idle instances. Active instances may still
return, but a late return is reset and destroyed instead of entering idle
retention.

The Scope releases an Entry's Prefab Source lease only after every idle, rented,
pending-destroy, and Temporal-retained physical instance reaches a terminal
state. A requested Unity destruction is not terminal by itself: Pooling waits
for its physical-destruction observer before releasing the source lease. It
disposes its Content Scope after all Entries finish. This order keeps source
ownership valid through the physical instance's final destruction callbacks.

`CoCoPoolHost` destruction has a force-cleanup fallback for leaked handles. It
invalidates generations, performs best-effort reset/destruction, waits for the
same terminal barrier, releases source ownership, and records leak/forced-
shutdown diagnostics.

Map has package-internal terminal access only to force-close a Pool Scope that
Map itself owns. It may use that path during terminal Map Host shutdown after
normal close cannot complete; it cannot force-stop the shared `PoolRuntime` or a
Scope owned by another consumer.

Map's graceful and terminal shutdown paths share this same owned-Scope barrier:
new Map operations are frozen, demand ownership is disposed, and participant
cleanup closes each Map-owned Scope before its corresponding Scene lease can be
released. Repeated Disable, Destroy, explicit shutdown, or Content-first
fallback therefore converge on one terminal task rather than force-closing the
shared Pool runtime more than once.

For Temporal records, Host stop first uses the normal state-aware release path.
An active record owns one matched Rent callback lease and therefore receives
exactly one reverse Return. A pending `TemporalInactive` record has not received
Rent and releases without Return; a quarantined record was already reset and
also releases without another Return. Force Destroy is reserved for unavailable
physical identity, reset/reparent failure, or callback re-entry.

Pooling also registers an internal dependency drain with its `ContentRuntime`.
If Content shutdown begins first, Pooling synchronously checks every Entry. A
runtime with no active, Temporal-retained, or quarantined ownership uses the
normal graceful shutdown path and returns no forced-shutdown warning. Idle,
pending-destroy, and cancelled-Prepare cleanup still waits for physical
termination before releasing source ownership. If any active or Temporal
ownership remains, Pooling uses forced shutdown, reports
`PoolForcedShutdown`, and preserves the same physical terminal barrier.
Correct ownership therefore does not depend on `MonoBehaviour.OnDisable` or
`OnDestroy` ordering.

## Temporal retained entities

`CoCoFlow.Runtime.Pooling.Temporal` is an explicit, Host-scoped sidecar. It is
used only when the same physical GameObject must remain available while a
`CoCoStateGraphHost` history can still project that entity as present.

```text
TryRent inactive handle
  -> bind entity data
  -> TryAdopt(CoCoTemporalEntityId, ref handle)
  -> TryActivate(entityId)
  -> TryDespawn(entityId)
```

Successful adoption transfers rental authority to `PoolTemporalRuntime` and
invalidates every copied consumer handle for the old generation. Consumers keep
the pure-value `CoCoTemporalEntityId` and may resolve the currently projected
GameObject for observation; they do not regain Return authority.

A despawned physical instance that remains reachable from retained history is
deactivated, reset, and quarantined. It cannot return to normal idle reuse.
Ring overwrite, branch-future discard, history clear, or Host stop releases it
only after the last historical reference expires.

Each adopted live record remembers its most recent activation parent. Scene
Root is an explicit parent state rather than an ambiguous null: replay performs
`SetParent(null, false)`. A live Transform parent is restored only while that
exact Transform remains available. The record refreshes the presentation parent
whenever `TemporalActive` exits, so a consumer reparent performed during the
active interval becomes the next replay target. A destroyed parent, destroyed
GameObject, or failed `SetParent` produces a structured failure and terminal
physical cleanup rather than a `MissingReferenceException`. The Transform
reference belongs to the live record only; it is cleared at terminal release
and never enters the Temporal ring or a Host snapshot.

`CoCoPoolTemporalBinding` composes Pool presence projection into the Host's one
synchronous Context restore slot. When Map retention is enabled, the complete
decorator chain is `Map -> optional Pool -> project restore binding`.
`IPoolTemporalApply` provides a separate, synchronous hook for reapplying entity
presentation after Context projection.

Before the StateGraph Host starts, its internal decorator introspection walks
the exact downstream component chain and rejects self-reference or an indirect
cycle such as `Map -> Pool -> Map`. This validation stays inside the Host-facing
adapter contracts and does not create a Map-to-Pool product dependency.

Each decorator freezes its exact downstream identity at Host attachment.
Identity, Unity liveness, Host boundary, and callback reentry are checked before
Pool mutation, before the downstream call, and after it returns. Rejection,
exception, replacement, destruction, or boundary escape stops after-restore
activation and uses the Host's existing world-correction contract; Pooling does
not claim an unprovable Unity transaction rollback.

Temporal history stores only entity and physical identity values. It never
stores a GameObject, Component, `PooledHandle`, `ContentLease`, backend handle,
or arbitrary world snapshot.

If a projected-only entity loses its physical instance while current authority
already requires it absent, the same Cancel or Correction may treat physical
absence as the desired result and finish Preview. A historical Preview or
Confirm that still requires that entity present continues to fail. Loss of an
authority-present entity remains a world-correction fault.

This sidecar is not multi-Actor or whole-world rollback. It does not reverse
physics, animation, navigation, already delivered side effects, or
durable persistence.

## Consumer fit

Good first-wave consumers are muzzle flashes, projectiles, repeated indicators,
virtualized inventory cells, and disposable enemies whose domain owner can
reset all instance state explicitly.

Do not use this module to pool Additive Scenes, permanent world roots, durable
entities, or objects whose ownership/reset contract is unknown. Raw local Unity
pooling remains available to project code outside CoCoFlow guarantees.

Pre10 Map may opt a participant slot into Pooling through an explicit compiled
plan. It does not pool Additive Scenes or permanent world roots, and it does not
grant a participant access to the shared Pool Runtime. The retained UI and Enemy
implementations keep their existing ownership behavior until their owning
downstream Pre explicitly adopts Pooling.

## Diagnostics

Runtime snapshots and the fixed-capacity ledger contain Pool/Scope/instance
identity, lifecycle state, capacity, active/idle/Temporal counts, hit/miss
counts, creation/destruction totals, and structured diagnostics. They never
retain Unity objects, source leases, handles, delegates, backend handles, or
exception objects.

Rental stack capture remains explicit and bounded. It should be disabled when
measuring the Ready idle-hit allocation path.

## Deferred

- generic non-GameObject pools and a custom public container;
- Unity versions below Unity 6;
- hard active/total caps and automatic trim/LRU;
- runtime hot profile mutation;
- direct Addressables ownership outside Content;
- automatic migration of UI, Enemy, or permanent scene objects;
- automatic Map pooling without an explicit participant/profile contract;
- multi-Actor or whole-world rollback;
- durable Temporal reconstruction or reflection-driven automatic cleanup.

See [Content Acquisition and Ownership](ContentOwnership.md) for source
ownership and [Temporal Rewind](TemporalRewind.md) for Host history and
authority rules. See [Map Region Fidelity](Module-Map.md) for committed-node
Scope ownership and transaction ordering.
