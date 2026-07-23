# Module: UI

> Pre8 integration status: `0.4.0-pre.8` · Updated 2026-07-23

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
panel. UI continues to own `Instantiate` and `Destroy` in Pre8.

Raw Addressables addresses and handles are no longer part of this module. A
panel button and the pause-panel binding use `ContentReference`; Direct and
Addressables sources therefore follow the same UI path.

## Deferred

Pre12 owns navigation queues, history/back behavior, loading overlays, focus
arbitration, transition interruption, and the final UI authoring model. Pre9
owns any future pooled UI instance path.
