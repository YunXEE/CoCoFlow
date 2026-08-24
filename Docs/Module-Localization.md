# Module: Localization

> Contract status: `0.4.0-rc.0` · Updated 2026-08-22

CoCoFlow depends directly on Unity's official
`com.unity.localization@1.5.9`. Localization remains a presentation module:
Core, State Flow, StateGraph, and Host assemblies do not reference it.
Addressables may arrive transitively through the official package, but this
does not change CoCoFlow Content's Direct/Addressables ownership contract.

## Assemblies

- `CoCoFlow.Runtime.Modules.Localization` owns diagnostics and compiles by
  default with the official package.
- `CoCoFlow.Runtime.Modules.Localization.UI` is an optional UI V2 extension
  that owns `UIWidgetLocalizedText`.
- `CoCoFlow.Runtime.Modules.Input.UI` is an optional UI V2 extension that
  composes binding prompts and glyphs.

The two UI extension assemblies follow the existing UI V2 dependency contract:
`COCOFLOW_UNITASK_SUPPORT`, `COCOFLOW_DOTWEEN_SUPPORT`, and
`UNITASK_DOTWEEN_SUPPORT` must all be enabled. A default installation without
those optional integrations still compiles Localization Core and Input Core,
but does not expose the Widget or Presenter.

## UIWidgetLocalizedText

The Widget references one `LocalizedString` and one `TMP_Text`. Enable
subscribes to `StringChanged`, Disable unsubscribes, and `ResetState()` requests
an immediate refresh. `SetArguments` replaces Smart String arguments, allowing
prompts such as `Press {binding}` to update in the current Screen.
Supplying new arguments also ends a previous `ClearPresentation()` suppression.
If no `LocalizedString` is configured, the Widget immediately displays the
current fallback and reports `MissingLocalizedString`; Reset and
Disable/Enable preserve that presentation.

`LastDiagnostic` distinguishes a missing target, missing LocalizedString,
invalid Table/Entry, load failure, and empty result. Every invalid result uses
the serialized fallback text and does not require the Screen to close and
reopen. If an asynchronous load fails while no `TMP_Text` target is configured,
the Widget reports `MissingTextTarget` without dereferencing the missing target
or throwing from its observation coroutine.

Localization does not own input binding selection. `InputPromptPresenter`
obtains the current display string and optional glyph from `InputReader`, then
passes the display string into this Widget.
