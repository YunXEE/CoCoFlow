# Module: Localization

> Pre14 contract: `0.4.0-pre.14` · Updated 2026-07-27

CoCoFlow depends directly on Unity's official
`com.unity.localization@1.5.9`. Localization remains a presentation module:
Core, State Flow, StateGraph, and Host assemblies do not reference it.
Addressables may arrive transitively through the official package, but this
does not change CoCoFlow Content's Direct/Addressables ownership contract.

## Assemblies

- `CoCoFlow.Runtime.Modules.Localization` owns diagnostics.
- `CoCoFlow.Runtime.Modules.Localization.UI` owns
  `UIWidgetLocalizedText`.
- `CoCoFlow.Runtime.Modules.Input.UI` composes binding prompts and glyphs.

## UIWidgetLocalizedText

The Widget references one `LocalizedString` and one `TMP_Text`. Enable
subscribes to `StringChanged`, Disable unsubscribes, and `ResetState()` requests
an immediate refresh. `SetArguments` replaces Smart String arguments, allowing
prompts such as `Press {binding}` to update in the current Screen.

`LastDiagnostic` distinguishes a missing target, missing LocalizedString,
invalid Table/Entry, load failure, and empty result. Every invalid result uses
the serialized fallback text and does not require the Screen to close and
reopen.

Localization does not own input binding selection. `InputPromptPresenter`
obtains the current display string and optional glyph from `InputRuntime`, then
passes the display string into this Widget.
