# Module: UI

> Pre9 integration status: `0.4.0-pre.9` · Updated 2026-07-23

The retained UI module now consumes Content for panel Prefab Source ownership.
Its navigation stack, input focus, pause/cursor policy, DOTween transitions, and
`UIManager` singleton remain transitional behavior owned by Pre12.

## Panel ownership

`UIManager` receives an explicit `CoCoContentHost` and opens a
`ContentReference` whose kind is Prefab Source. Each successful panel instance
owns an independent Content Scope/Lease binding:

1. Content acquires the prefab source.
2. UI validates `UIPanelBase` and instantiates the GameObject.
3. The source lease remains alive while that instance exists.
4. Instance destruction releases its ownership binding.
5. The last source lease starts backend release.

The Content lease is not an instance handle and does not pool or reuse the
panel. UI continues to own `Instantiate` and `Destroy` after Pre9.

Raw Addressables addresses and handles are no longer part of this module. A
panel button and the pause-panel binding use `ContentReference`; Direct and
Addressables sources therefore follow the same UI path.

## Pooling boundary

The Pre9 Pooling contract can support project-owned virtualized inventory cells
and other repeated UI elements, but this retained UI module is not migrated.
`UIManager` does not create a `PoolScope`, return `PooledHandle` values, or
silently replace its existing panel ownership.

A downstream UI implementation may adopt Pooling only when it can define an
explicit reset/bind/activate contract for every pooled element. Pooling remains
Content-backed, so optional Addressables still enter through
`ContentReference` rather than through UI or Pool-owned handles.

## Deferred

Pre12 owns navigation queues, history/back behavior, loading overlays, focus
arbitration, transition interruption, the final UI authoring model, and any
decision to migrate retained panel or repeated-cell consumers to Pooling.
