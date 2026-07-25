# Module: Map

> Pre9 integration status: `0.4.0-pre.9` · Updated 2026-07-23

The retained Map module now consumes Content for requester-scoped Additive Scene
ownership. It remains a small trigger-driven bridge; Region, Chunk, distance,
priority, prefetch, replay, and production streaming policy belong to Pre10.

## Requester-scoped demand

Every logical requester has a stable `ContentOwnerId` and one Content Scope.
Within that requester, demanding the same `ContentId` twice is idempotent.
Different requesters receive distinct scene leases even when Content shares one
physical backend load.

Releasing requester A removes only A's exact demand. The scene remains loaded
while requester B still owns a lease, and the final scene lease starts unload.
Load failure removes the failed demand so a later explicit demand may retry.

`MapStreamTrigger` references the intended `MapResourceManager` directly. This
keeps requests inside the correct project/world runtime instead of broadcasting
release authority globally. A loaded notification may still be published for
observation, but it carries requester and Content identity and cannot release a
scene.

The module no longer stores Addressables handles or backend addresses. Direct
SceneManager and optional Addressables scenes use the same Content request path.

## Pooling boundary

Pre9 does not pool Additive Scenes or migrate Map consumers. Scene residency
continues to use requester-scoped Content leases; a scene is unloaded only
through that ownership boundary.

A project or later Map policy may create an explicit Pool Scope for transient
objects spawned inside a loaded region, such as disposable enemies or effects.
That instance policy remains separate from Region/Chunk demand and cannot keep,
release, or substitute the scene lease.

## Deferred

Pre10 owns the final Map Region/Chunk vocabulary, desired-set resolution,
distance and adjacency policy, activation sequencing, race orchestration,
Temporal barriers, streaming diagnostics, and replay behavior.
