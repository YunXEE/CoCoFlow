# Module: Input

> Documentation baseline: `0.4.0` · Updated 2026-08-29

The Input module owns the Unity Input System boundary. `PlayerInput`, its
runtime `InputActionAsset`, actions, devices, control scheme, bindings, and
rebind overrides remain the physical-input authority. StateGraph consumes the
unmanaged `RawInputIntent`; it never reads an `InputAction`, device callback, or
binding path.

## RawInputIntent broadcast

Keep PlayerInput and InputReader on a persistent object. On each Host, add an
InputIntent, assign its Reader, and put InputIntent in the Host Intent Sources.
Several Hosts can reference the same Reader. Source list order is not a type ID.

Each gameplay InputSystem update (Dynamic, Fixed or Manual) replaces the latest
image, even when no action fires. Performed/Canceled callbacks for the same
action overwrite its previous phase within that update; Performed then Canceled
therefore leaves Canceled. Actuated Move/Look/Zoom convenience bindings append
Held records at the end of the update, while the eight-record capacity permits.

InputIntent implements ICoCoIntentFrameSource<RawInputIntent> and only copies
Reader.TryReadLatest. Reading never drains, advances, acknowledges or replays
input. Hosts may see the same image repeatedly, or miss a short pulse while
paused. Resuming reads the latest update, with no backlog. The optional legacy
TryConsumeBufferedAction convenience API is separate from this broadcast.

The RawInputIntent type, stable ID, reducer and eight-record layout are retained.
No extra queue or per-Host cursor is created. The action-name encoding is the
existing CoCoFixedString64 encoding. Capacity overflow is dropped, not deferred.
Missing, disabled or uninitialized Reader returns false/default. FenceInput
clears the latest image for every observer. Disabling one InputIntent has no
effect on another observer or on the Reader.

Migration: replace every Host Intent Sources reference to InputReader with its
local InputIntent. Programmatic callers use InputIntent.TrySample or the
non-consuming InputReader.TryReadLatest; InputReader.TrySample is removed.
StateLogic continues declaring [Intent] public RawInputIntent Input { get; set; }.
CameraAimCoupler and other TryReadValue callers require no migration.

## Convenience and event surface

`InputReader` also exposes current `MoveInput`, `LookInput`, and `ZoomInput`,
typed `TryReadValue`, performed/canceled `ActionChanged`, action-name events,
prompt changes, and a short optional one-action input buffer. These are local
Unity-facing conveniences; only `RawInputIntent` crosses into StateFlow.

`TryReadValue` fails with `false/default` while the Reader is disabled, not
initialized, changing maps/bindings, or neutral-gating an action. `FenceInput`
clears the latest input image, short buffer, and convenience snapshots, then publishes
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

- The latest image has eight records; overflow is dropped without replay.
- `InputReader` samples one local `PlayerInput`; it does not aggregate players,
  network input, AI, replay files, or platform account state.
- Rebind storage and conflict policy are project-owned.
- This document describes current behavior but makes no maturity classification
  for the Input module in 0.4.0.
