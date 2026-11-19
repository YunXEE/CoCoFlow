# Module: Input

The Input module owns the Unity Input System boundary. `PlayerInput`, its
runtime `InputActionAsset`, actions, devices, control scheme, bindings, and
rebind overrides remain the physical-input authority. StateGraph consumes the
typed `InputIntentData`; it never reads an `InputAction`, device callback, or
binding path.

## InputIntentData broadcast

Keep PlayerInput and InputReader on a persistent object. On each Host, add an
InputIntent, assign its Reader, and put InputIntent in the Host Intent Sources.
Several Hosts can reference the same Reader. Source list order is not a type ID.

Once a Host binds the source, the Reader retains Performed and Canceled changes
across gameplay Input System updates until the bound Hosts have committed them.
`Changes` preserves arrival order, including repeated actions. `Sequence` orders
records with equal timestamps. `InputTime` is the original Unity input time in seconds.

Each Host Tick freezes its own unread changes and samples `Current` at the same
boundary. `Current` contains pressed buttons and actuated Move, Look and Zoom
as Held records, including on Ticks without changes. A retry of an unresolved
Tick uses its frozen data. Successful commit advances only that Host's cursor.
A fence clears pending changes and increments `FenceGeneration`.

StateLogic declares `[Intent] public InputIntentData Input { get; set; }`.
`Count`, `TryGet` and `TryFind` read changes followed by current state.
Producers constructing their own `InputIntentData` supply read-only views of
arrays they keep immutable after delivery. Code that used the slot setters
must migrate to the constructor. With managed array references, the payload
can no longer be used as an argument to an `unmanaged`-constrained generic.
Use direct Reader queries or Action events in place of `TryReadLatest`.

## Convenience and event surface

`InputReader` also exposes current `MoveInput`, `LookInput`, and `ZoomInput`,
typed `TryReadValue`, button `TryReadPressed`, performed/canceled `ActionChanged`,
action-name events, prompt changes, and a short optional one-action input buffer. These are local
Unity-facing conveniences; only `InputIntentData` crosses into StateFlow.

Both direct queries respect action enablement and neutral gating. Direct
queries and Action subscriptions leave all Host cursors untouched. `FenceInput`
clears pending changes, the short convenience buffer and cached axis values,
then publishes `InputFenced`.

Switching action maps and binding changes fence input before and after the
controlled transition. Actions that were active across a transition remain
neutral-gated until their controls return to rest, preventing a held input from
appearing as a fresh action on the new side of the boundary.

## Rebinding

`InputRebindController` starts interactive rebinding by stable Action ID and
Binding ID. It temporarily disables the target action, captures the previous
override JSON, and commits only after the configured
`IInputBindingOverrideStore` accepts the new JSON. Cancellation or persistence
failure restores the prior overrides and action enablement.

`InputReader` loads overrides once after runtime initialization. The store is a
project-provided `MonoBehaviour` implementing `IInputBindingOverrideStore`;
CoCoFlow does not prescribe PlayerPrefs, files, cloud storage, or account
policy.

## Prompt presentation

`TryGetPrompt` selects a binding for the current control scheme/device and
returns `InputPromptSnapshot` with display text, device layout, control path,
and an optional glyph. `InputGlyphCatalog` resolves an exact device layout
first and then compatible base layouts. When no glyph exists, presentation
falls back to the binding display string.

`InputPromptPresenter` lives in the optional Input UI assembly and renders that
snapshot; prompt presentation does not become gameplay authority.

## Boundaries

- `InputReader` samples one local `PlayerInput`; it does not aggregate players,
  network input, AI, replay files, or platform account state.
- Rebind storage and conflict policy are project-owned.
- This document describes current behavior but makes no maturity classification
  for the Input module in 0.4.2.
