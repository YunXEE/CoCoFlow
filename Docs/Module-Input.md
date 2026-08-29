# Module: Input

> Documentation baseline: `0.4.0` · Updated 2026-08-29

The Input module owns the Unity Input System boundary. `PlayerInput`, its
runtime `InputActionAsset`, actions, devices, control scheme, bindings, and
rebind overrides remain the physical-input authority. StateGraph consumes the
unmanaged `RawInputIntent`; it never reads an `InputAction`, device callback, or
binding path.

## RawInputIntent sampling

`InputReader` requires `PlayerInput` and implements
`ICoCoIntentFrameSource<RawInputIntent>`. It maintains a fixed 32-record queue
for performed/canceled callbacks. On one accepted CoCoTick, `TrySample`:

1. records the active action-map name;
2. drains discrete records in arrival order into the eight-record
   `RawInputIntent` capacity;
3. appends one `Held` record for each actuated Move, Look, or Zoom convenience
   action while capacity remains.

Each record contains the action name, values, phase, and a monotonically
increasing source sequence. A full callback queue drops the newest record. If
more discrete records are drained than fit in one Intent, the excess is
dropped; it is not carried into a second hidden command frame.

The Standard Binding provider binds this source to the package RawInput Intent
lane and uses the pass-through reducer. Project state scripts consume semantic
records through the engine-free contracts in
`Runtime/Core/Contracts/State/CoCoRawInput.cs`.

## Convenience and event surface

`InputReader` also exposes current `MoveInput`, `LookInput`, and `ZoomInput`,
typed `TryReadValue`, performed/canceled `ActionChanged`, action-name events,
prompt changes, and a short optional one-action input buffer. These are local
Unity-facing conveniences; only `RawInputIntent` crosses into StateFlow.

`TryReadValue` fails with `false/default` while the Reader is disabled, not
initialized, changing maps/bindings, or neutral-gating an action. `FenceInput`
clears the raw queue, short buffer, and convenience snapshots, then publishes
`InputFenced`.

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

- The queue and `RawInputIntent` have fixed capacities; overflow is a drop
  policy, not backpressure.
- `InputReader` samples one local `PlayerInput`; it does not aggregate players,
  network input, AI, replay files, or platform account state.
- Rebind storage and conflict policy are project-owned.
- This document describes current behavior but makes no maturity classification
  for the Input module in 0.4.0.
