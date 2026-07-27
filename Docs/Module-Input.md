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
continuous reads, and publishes prompt and input-fence changes.

## Input fence

Action Map changes, rebind changes, source disable, Host suspension, Temporal
Preview/restore, and resume boundaries clear pending discrete commands and
continuous snapshots. A generated `ProjectPlayerIntentSource` accepts callbacks
only while its Host is Running and not Previewing. After a fence it waits for
new physical action callbacks, so a held pre-suspend value is not replayed on
resume.

## Rebind and prompts

`InputRebindController` identifies the Action and Binding by stable Input
System IDs. It snapshots the previous override JSON before starting. Completion
persists through `IInputBindingOverrideStore` and refreshes the prompt; cancel,
operation failure, or storage failure restores the previous overrides.

`InputGlyphCatalog` resolves an exact device-layout/control-path entry first,
then an Input System base layout. `InputPromptPresenter` falls back to the
binding display string and passes that display as a Smart String argument to
`UIWidgetLocalizedText`.

## Compatibility

`InputReader`, `CoCoInputIntent`, `IInputStateProvider`,
`IInputEventSource`, and `IInputModeController` are obsolete transition
surfaces. `InputRuntime` implements the three interfaces explicitly for the
retained UI/Camera consumers. New project code and generated scaffold code use
`ICoCoIntentFrameSource<TIntent>` instead.
