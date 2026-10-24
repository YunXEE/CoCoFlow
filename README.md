![CoCoFlow](.github/assets/cocoflow-banner.svg)

> **Version**: 0.4.6 · **Unity**: 6000+
>
> [简体中文](README.zh-CN.md)

CoCoFlow is a Unity 6 state-flow framework built around typed input, a layered
StateGraph, transactional Context commits, Temporal restore, and explicit
runtime ownership.

## New in 0.4.6

- Each Layer can bind one optional `CoCoAnyStateLogic`. It updates after the
  ordinary active path and takes transition priority when its request is eligible.
  It keeps the complete Config/Memory/Intent/Operation and mailbox surface,
  including existing transaction and restore rules. Its Memory survives leaf
  changes; re-entering the current leaf resets that leaf's Activation, Memory and
  clock while preserving its ancestors and AnyState. Authors can request this
  every Tick. See the [runtime contract](Docs/StateGraphRuntime.md#anystate-one-persistent-logic-per-layer)
  and [Editor setup](Docs/StateGraphEditor.md#configuring-anystate).
- Collapse sub-state machines below **Tidy Subtree**. Cross-boundary transitions
  share a line with one triangle per real transition; a menu selects the original
  transition. Folding stays in the Editor session and preserves graph data and
  Runtime inputs. Purple highlights remain visible when folded; larger triangles,
  a persistent rose AnyState border and quieter genealogy lines improve reading.
  See [submachine folding](Docs/StateGraphEditor.md#collapsing-a-sub-state-machine).
- Author PersistenceContext IDs explicitly on Prefab assets, with separate variant
  overrides. Lock ID, read-only Scan and Regenerate ID support this workflow;
  lifecycle callbacks no longer generate or deduplicate IDs. Existing IDs remain;
  regeneration changes save identity. Multiple independently saved entities need
  distinct identities. See [identity boundaries](Docs/Module-Persistence.md#stable-entity-identity).

Graphs without AnyState need no migration or additional transitions. Player,
IL2CPP/AOT, stripping and complete Umbra animation/foot IK remain unverified.
See the [changelog](CHANGELOG.md) for fixes and compatibility boundaries.

## What “mature” means

For this release, **mature** means that the public Runtime API is stable, the
module has been used in a real project to complete its stated responsibility,
and its known boundaries are documented. Explicit changes in the release's
changelog take precedence over compatibility expectations. It does **not** mean newest
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
Host-local Intent sources       External struct events
          ↓                               ↓
      IntentFrame                  Adapter → Inbox batch
          └──────────────┬────────────────┘
               Layered StateGraph Step
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

Each actor owns its runtime state. StateLogic reads immutable Tick input,
updates activation Memory, writes declared Operation Sections, and stages
outgoing events. The Host validates bindings before
start, stages one candidate tick, and either commits the full actor transaction
or keeps the previous authority unchanged. Cross-object effects leave through
the committed Event Outbox rather than direct state callbacks.

Temporal restore is same-session and exact-layout. Durable save data is a
separate Persistence schema; the two are intentionally not the same wire
format.

## Install

Add the package from Unity Package Manager with the Git URL:

```text
https://github.com/YunXEE/CoCoFlow.git#v0.4.6
```

Or add this entry to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.yunxee.cocoflow": "https://github.com/YunXEE/CoCoFlow.git#v0.4.6"
  }
}
```

Use **CoCoFlow > Utility Panel** (the CoCoFlow Utility window) to inspect optional
integration dependencies and project settings. Some integration assemblies are
compiled only when their external packages and support defines are present; see
the [dependency matrix](Docs/DependencyMatrix.md).

## Documentation

- [Documentation index](Documentation~/index.md)
- [StateGraph Asset and Compiler](Docs/StateGraphCompiler.md)
- [StateGraph Runtime and Host](Docs/StateGraphRuntime.md)
- [State Flow / Event Boundary](Docs/ContextNetworkBoundary.md)
- [Temporal Rewind](Docs/TemporalRewind.md)
- [Input](Docs/Module-Input.md)
- [Camera](Docs/Module-Camera.md)
- [Persistence](Docs/Module-Persistence.md)
- [UI](Docs/Module-UI.md)
- [Map](Docs/Module-Map.md)
- [Pooling](Docs/ObjectPooling.md)
- [Changelog](CHANGELOG.md)

## Release policy

CoCoFlow 0.4.x will iterate in small steps. Mature Runtime surfaces are treated
as stable APIs, with explicit breaking changes documented per release. Map and
Pooling may change their APIs, configuration, and serialized structures during
0.4.x. Unrated modules should be evaluated from
their implementation and module documentation rather than inferred from this
table.

## License

MIT
