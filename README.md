# CoCoFlow

> **Version**: 0.4.0 · **Unity**: 6000+
>
> [简体中文](README.zh-CN.md)

CoCoFlow is a Unity 6 state-flow framework built around typed input, a layered
StateGraph, transactional Context commits, Temporal restore, and explicit
runtime ownership. Version 0.4.0 closes the current Runtime line as a usable
release: it stops feature expansion and documents the boundaries that exist in
the package today.

## What “mature” means

For this release, **mature** means that the public Runtime API is stable, the
module has been used in a real project to complete its stated responsibility,
and its known boundaries are documented. It does **not** mean newest
architecture, maximum performance, complete Editor tooling, zero defects, or
marketplace certification.

| Module | Status | Accurate scope |
|---|---|---|
| Core Engine | **Mature** | Contracts, StateFlow, StateGraph, StateGraphAuthoring Runtime, and StateGraphHost; the native 0.4 core. |
| Camera | **Mature** | Originated in 0.3.9; the current Rig, priority, and mode APIs are stable and usable. |
| Persistence | **Mature** | Originated in 0.3.9; supports schema v2, Containers, and StateGraph ContextFrame persistence. |
| UI | **Mature** | Originated in 0.3.9; Panel, Widget, Input, and Content-ownership APIs are stable and usable, but this is not a high-performance UI framework. |
| Map | **Immature** | The current implementation is usable, but public APIs and configuration/serialized shapes are not compatibility-guaranteed. |
| Pooling | **Immature** | The current implementation is usable, but public APIs and configuration/serialized shapes are not compatibility-guaranteed. |
| Other modules | **Unrated** | No maturity or immaturity judgment is made for this release. |

The Core Engine maturity statement does not include the StateGraph Editor or
the older `Runtime/Core/*.cs` EventBus, Services, and Context facilities.

## Core flow

```text
Raw input / typed events
        ↓
Mailbox + Intent arbitration
        ↓
Layered StateGraph step
        ↓
Finalized OperationFrame
        ↓
Operators + staged Context candidate
        ↓
Atomic commit
        ├─ committed ContextFrame
        ├─ Event Outbox + Trace
        └─ Temporal / persistence projection
```

Each actor owns its runtime state. StateLogic evaluates immutable input and
writes only declared Operation Sections. The Host validates bindings before
start, stages one candidate tick, and either commits the full actor transaction
or keeps the previous authority unchanged. Cross-object effects leave through
the committed Event Outbox rather than direct state callbacks.

Temporal restore is same-session and exact-layout. Durable save data is a
separate Persistence schema; the two are intentionally not the same wire
format.

## Install

Add the package from Unity Package Manager with the Git URL:

```text
https://github.com/YunXEE/CoCoFlow.git#v0.4.0
```

Or add this entry to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.yunxee.cocoflow": "https://github.com/YunXEE/CoCoFlow.git#v0.4.0"
  }
}
```

Use **Tools > CoCoFlow > Setup Assistant** to inspect optional integration
dependencies and project settings. Some integration assemblies are compiled
only when their external packages and support defines are present; see the
[dependency matrix](Docs/DependencyMatrix.md).

## Documentation

- [Documentation index](Documentation~/index.md)
- [StateGraph Asset and Compiler](Docs/StateGraphCompiler.md)
- [StateGraph Runtime and Host](Docs/StateGraphRuntime.md)
- [State Flow / Event Boundary](Docs/ContextNetworkBoundary.md)
- [Temporal Rewind](Docs/TemporalRewind.md)
- [Camera](Docs/Module-Camera.md)
- [Persistence](Docs/Module-Persistence.md)
- [UI](Docs/Module-UI.md)
- [Map](Docs/Module-Map.md)
- [Pooling](Docs/ObjectPooling.md)
- [Changelog](CHANGELOG.md)

## Release policy

CoCoFlow 0.4.x will iterate in small steps. Mature Runtime surfaces are treated
as stable APIs. Map and Pooling may change their APIs, configuration, and
serialized structures during 0.4.x. Unrated modules should be evaluated from
their implementation and module documentation rather than inferred from this
table.

## License

MIT
