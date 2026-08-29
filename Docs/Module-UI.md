# Module: UI

> **Maturity: Mature** · Documentation baseline: `0.4.0` · Updated 2026-08-29
>
> The module originated in 0.3.9. Its Panel, Widget, Input, and Content
> ownership APIs are stable and usable. This maturity statement does not claim
> that UI is a high-performance or large-scale interface framework.

The UI module provides a compact stack-based panel runtime, panel/widget base
classes, input-map integration, Content-backed prefab ownership, and DOTween
transitions.

## Current runtime

`UIManager` is a scene singleton. Its public surface opens, toggles, closes, or
closes all panels through `ContentReference` values of kind `PrefabSource`.

```text
ContentReference
      ↓ acquire Prefab Source
Content Scope + Lease
      ↓ Instantiate
UIPanelBase → panel stack → ShowAsync / HideAsync
      ↓ Destroy
release panel ownership → release source lease
```

Each successfully opened panel receives a distinct `ContentOwnerId`,
`ContentScope`, and source `ContentLease<GameObject>`. The lease keeps the
prefab source available while that instance exists. A
`UIPanelSourceOwnership` component releases the scope and lease when the panel
instance is destroyed. A lease owns the source, not the instantiated object.

Panel roots are selected from `UILayer`: HUD panels use `hudRoot`, Popup panels
use `popupRoot`, and other stack panels use `panelRoot`.

## Navigation and panel policy

The manager keeps a LIFO `Stack<UIPanelBase>` and serializes transitions with
one `_isTransitioning` gate. Calls that arrive while another transition is in
progress are ignored rather than queued.

`UIPanelConfig` controls the current policy:

| Flag | Current behavior |
|---|---|
| `PauseGame` | Reference-counts pause locks and sets `Time.timeScale` to `0`; the last release restores `1`. |
| `TakeInputFocus` | Switches the bound `InputReader` to the configured UI action map. |
| `HideLowerPanels` | Disables interaction on the panel directly below; it does not virtualize or unload that panel. |
| `ShowCursor` | Reference-counts cursor locks and shows/unlocks the cursor until the last release. |

The optional Pause and Cancel action references open the configured pause panel
or pop the current panel when their performed events arrive. When the stack
becomes empty, the manager switches back to the configured player action map.

`UIPanelBase` requires a `CanvasGroup` and implements asynchronous scale/fade
show and hide transitions through UniTask and DOTween. Derived panels can use
`OnBeforeShow` and `OnAfterHide` for local lifecycle work.

## Widgets and scene UI

`UIWidgetBase` requires a `CanvasGroup`, discovers its owning `UIPanelBase` or
`UISceneBase`, resets on enable, and exposes a consistent interactable state.
The package includes button, slider, selector, indicator, and panel examples.

`UISceneBase` represents world-space or scene UI that does not participate in
the panel stack. `UIWidgetContainer` provides deterministic row, column, and
grid placement plus an Editor preview; its Dynamic mode reserves layout slots
but does not create a virtualized data source.

Localization UI and Input prompt integrations live in separate optional
assemblies. They extend the widget surface without changing panel navigation or
ownership.

## Efficiency boundaries

The following are current implementation limits, not reasons to treat the API
as unstable:

- panels are opened with `Instantiate` and closed with `Destroy`;
- `UIManager` remains a singleton with one panel stack and serial transitions;
- there is no automatic panel or widget Pooling;
- there is no virtualized list/data-view system;
- calls are not designed for high-throughput concurrent navigation;
- transition interruption and queueing are not provided.

Projects that need pooled cells or panels must define their own reset, bind,
activate, and ownership rules. The separate Pooling module is not silently
inserted into UI.

## Dependencies and boundaries

The UI assembly is enabled only when its UniTask and DOTween support defines
are present. It also integrates with Content, Input System, and TextMeshPro.
Raw Addressables handles are not exposed by UI; optional Addressables sources
enter through `ContentReference`.

“Mature” applies to the existing Runtime API and documented behavior. It does
not include a claim of optimal performance, complete Editor authoring, automatic
focus arbitration beyond the current stack, or marketplace readiness.
