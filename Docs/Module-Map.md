# Module: Map

> Pre8 integration status: `0.4.0-pre.8` · Updated 2026-07-23

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

## Deferred

Pre10 owns the final Map Region/Chunk vocabulary, desired-set resolution,
distance and adjacency policy, activation sequencing, race orchestration,
Temporal barriers, streaming diagnostics, and replay behavior.
