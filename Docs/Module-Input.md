# Module: Input

> Pre14 contract: `0.4.0-pre.14` · Updated 2026-07-27

The Input module turns Input System actions into project-owned semantic Intent.
`PlayerInput`, its runtime `InputActionAsset`, `InputUser`, devices, Control
Scheme, bindings, and rebind overrides remain the physical-input authority.
StateGraph never reads an `InputAction`, device, callback, or binding path.

## Tick model

- Continuous values use the latest snapshot.
- Performed and Canceled callbacks enter a fixed ring FIFO in callback order.
- `InputCommandQueue<TCommand>` defaults to 32 entries. A full queue rejects the
  newest callback and increments `OverflowCount`.
- One CoCoTick freezes at most eight entries into the unmanaged
  `InputCommandBatch<TCommand>`. Remaining entries stay queued for the next
  Tick.
- One frozen project Intent is shared read-only by every relevant State/Layer;
  there is no per-State consume API.

`InputRuntime` uses the exact runtime Action collection owned by `PlayerInput`;
it does not clone a second `InputActionAsset`. It reports the current Control
Scheme and device layout, resolves binding display strings and glyphs, exposes
continuous reads, and publishes prompt and input-fence changes. If
`PlayerInput.actions` is replaced while enabled, `InputRuntime` unsubscribes the
cached old collection, fences pending input, then subscribes the replacement.
Action/Map Disable fences after Input System has synchronously emitted its
Canceled callbacks. Re-enabled Actions whose controls are still actuated enter
a neutral gate: Performed/Canceled callbacks remain suppressed until every
bound control returns to its default value, and only the next physical input is
accepted.

## Input fence

Action Map changes, rebind changes, source disable, Host suspension, Temporal
Preview/restore, and resume boundaries clear pending discrete commands and
continuous snapshots. A generated `ProjectPlayerIntentSource` accepts callbacks
only while its Host is Running and not Previewing. After a fence it waits for
new physical action callbacks, so a held pre-suspend value is not replayed on
resume.

`CoCoStateGraphHost.InputAuthorityRevision` makes the boundary explicit. Every
actual lifecycle or state-restoration authority change advances the revision,
including same-frame Suspend/Resume and Preview/Cancel or Preview/Confirm
pairs. The generated Source compares both lifecycle acceptance and revision,
so returning to `Running` in the same frame cannot make an older callback valid
again.

## Rebind and prompts

`InputRebindController` identifies the Action and Binding by stable Input
System IDs. It snapshots the previous override JSON before starting. Completion
persists through `IInputBindingOverrideStore` and refreshes the prompt; cancel,
operation failure, or storage failure restores the previous overrides.

`InputGlyphCatalog` resolves an exact device-layout/control-path entry first,
then an Input System base layout. The optional UI V2
`InputPromptPresenter` falls back to the binding display string and passes that
display as a Smart String argument to `UIWidgetLocalizedText`. This presenter
assembly is available only when the existing UI V2 UniTask, DOTween, and
UniTask.DOTween support defines are all enabled; Input Core remains available
without them.

Prompt binding selection prefers the current Control Scheme binding group and
last-used exact/base device layout, then the binding group alone, the last-used
device, and finally authored order. With no callback history, it falls back to
`PlayerInput.devices`. Scheme, actual device, rebind, and Action Asset changes
all refresh the current Screen. An invalid Snapshot clears visible text,
fallback, glyph, and Smart arguments; late Localization callbacks remain
suppressed until a new valid presentation is supplied.

The generated `ProjectPlayerIntentSource` is the only Input-to-Graph bridge.
It converts Unity `Vector2` and `InputCommandBatch<ProjectPlayerCommand>` into
the engine-free `ProjectMoveValue` and `ProjectPlayerCommandBatch`. Generated
Graph authoring contracts therefore do not reference Input System,
`InputRuntime`, Unity types, or another CoCoFlow module.

## Compatibility

`InputReader`, `CoCoInputIntent`, `IInputStateProvider`,
`IInputEventSource`, and `IInputModeController` are obsolete transition
surfaces. `InputRuntime` implements the three interfaces explicitly for the
retained UI/Camera consumers. New project code and generated scaffold code use
`ICoCoIntentFrameSource<TIntent>` instead.
