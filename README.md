# CoCoFlow

[English](README.md) | [简体中文](README.zh-CN.md)

> **Version**: 0.4.0-pre.1 · **Unity**: 6000+
>
> This is the Pre1 Core-contract release. It freezes architectural boundaries and
> value semantics; it is not the finished 0.4 runtime or authoring workflow.

CoCoFlow is a Unity 6 framework for layered hierarchical finite-state machines
(Layered HFSM), Context-driven decisions, explicitly bounded Operations, and
host-driven deterministic ticks. The 0.4 line targets new single-player 3D
adventure and action projects.

## What Pre1 Freezes

Pre1 establishes the dependency direction that later prereleases must follow:

```text
explicitly bound Sources
  -> framework-owned ContextRuntime sample and merge
  -> Frozen Context Frame N
  -> independent Layered HFSM Layers
  -> declared Operation entry points
  -> Operation write-back
  -> Frozen Context Frame N + 1
```

The frozen Core surface covers:

- distinct graph, layer, state, transition, graph-instance, activation, and
  timeline identities;
- execution sequence, timeline tick/position, clock-domain, and tick-frame
  value contracts;
- explicit runtime lifecycle states;
- structured diagnostic domains, codes, severities, and records;
- pure-C# StateLogic roles and dependency declarations that do not expose
  `MonoBehaviour`, `GameObject`, Animator, or Playable types.

Core rules:

- StateLogic reads one frozen Context frame and cannot write it.
- Context sections declare only public abstract instance properties with
  parameterless getters. Indexers, default/static members, fields, callbacks,
  Unity objects, reference-backed collections, native handles, and stack-only
  values are rejected. A direct fact may be an immutable string; composite value
  facts must be recursively reference-free and therefore cannot contain strings.
  Section reads carry a matching `CoCoContextSectionRequirement` instead of
  exposing a mutable root, Source, Writer, or concrete provider type.
- Every Layer owns an independent HFSM and resolves one active leaf path.
- Layers execute by explicit priority; a lifecycle phase completes for one
  active path before the next Layer is processed.
- Unity callbacks are host inputs, not the CoCo clock. Variable, Fixed, and
  Manual drivers may produce CoCo ticks independently of Unity callback count.
- Zero or negative delta is invalid. Suspend produces no tick and therefore no
  frozen-frame sample.
- A Runtime may move directly from Created to Disposed before its first Run;
  Running or Suspended instances must Stop before Dispose.
- Operations are the approved side-effect boundary. Their writes become visible
  only in a later frozen frame. StateLogic submits unmanaged command values
  through a declared `CoCoOperationPortRequirement`, so no managed reference,
  delegate, shared result, or synchronous gameplay return channel crosses
  Submit. Pre5 additionally validates Port/Command affinity and rejects native
  handles and pointer-bearing command shapes before dispatch.
- Pre1 freezes the framework-provided StateLogic role, not a CLR security
  sandbox for arbitrary project code. State authoring assembly and dependency
  enforcement belongs to the StateGraph Compiler/authoring validation Pres.

The frozen authoring boundary for later Pres is:

```text
CoCoStateGraphAsset      1 : N  GraphRuntimeInstance
GraphRuntimeInstance     1 : 1  CoCoContextRuntime / Context Frame stream
CoCoContextRuntime       1 : N  explicit Context Source bindings
Frozen Context Frame     1 : N  independent Layers
```

Projects do not author an aggregate Root Context or wire a Context Provider to a
graph. The future `CoCoStateGraphHost` is the single Unity-facing framework
component on an actor: users select an asset and explicitly configure Source,
Operation, priority, ownership, and clock/driver bindings. The framework validates
the complete configuration before Running and keeps it fixed while Running. This
does not introduce a second Context graph or visual-scripting surface.

See [Context / Network Boundary](Docs/ContextNetworkBoundary.md) for the complete
frame and adapter rules.

## Transitional Repository State

The existing 0.3.9 CCS Runtime remains in the repository temporarily so Pre1 can
freeze contracts without combining that work with a runtime rewrite. It is
scheduled to be replaced in Pre4 and is not a 0.4 compatibility promise, API
baseline, or migration layer.

In particular:

- existing `CoCoStateController`, `CoCoStateLayer`, `CoCoStateBase`, and their
  Unity-lifecycle behavior are legacy implementation evidence only;
- 0.3.9 projects stay on the 0.3.9 revision; 0.4 does not ship a dual runtime;
- Pre1 publishes no Samples or Add-on import surface;
- the 0.3.9 read-only graph inspection tool was removed in Pre1; GraphAsset
  compilation and graph editing arrive in their dedicated Pres.

## Package Boundary

```text
Runtime/Core/Contracts   frozen engine-independent contracts
Runtime/Core             transitional 0.3.9 CCS Runtime until Pre4
Runtime/Gameplay         transitional gameplay implementations
Runtime/Modules          transitional presentation and service modules
Editor                   dependency/setup and legacy module tooling
Tests                    contract, architecture, and transition regressions
```

The Core Contracts assembly must not depend on Gameplay, presentation modules,
Editor code, project code, Animator, or Playables. Higher-level modules may
depend on Core contracts; Core must never depend back on them.

## Not Implemented in Pre1

Pre1 intentionally does not implement:

- Context V2 runtime composition, generated/compiled Section views, and source
  resolution;
- `StateGraphAsset`, graph compilation, transition editing, or runtime execution;
- the unified `CoCoStateGraphHost` and its binding Inspector;
- clock schedulers, time scaling, transition queues, or runtime snapshots;
- Operation ownership/claim arbitration and write-back implementations;
- temporal rewind;
- Playable-based animation, a CoCo animation runtime, combo authoring, or root
  motion ownership;
- starter content, gameplay templates, or a golden-path project; replacement
  Samples and the Adventure Starter are owned by Pre15/Pre16.

Those features belong to later prereleases and must build on the contracts
frozen here.

## Dependencies

The dependency set remains unchanged during Pre1 because the transitional
0.3.9 modules still compile against it.

| Package | Version | Current owner |
|---|---:|---|
| Addressables | 2.9.1 | Map and UI runtime workflows |
| Input System | 1.18.0 | Input module |
| Newtonsoft Json | 3.2.2 | Persistence module |
| Cinemachine | 3.1.6 | Camera module |
| AI Navigation | 2.0.0 | Character and Enemy navigation |
| Mathematics | 1.3.3 | Enemy/spline assemblies |
| Splines | 2.6.0 | Enemy spline support |

Dependency pruning belongs to the Pre that replaces each owning module, not to
the Core-contract freeze.

## Installation and Validation

Install the package through Unity Package Manager with a Git revision, or place
it in a Unity project's `Packages/` directory. Use an explicit prerelease tag or
commit; do not treat a moving development branch as a production dependency.

`CoCoFlow/Setup/Setup Assistant` is limited to dependency and support-define
status during this phase. It does not install project content.

Because this repository is a UPM package rather than a complete Unity project,
the release gate requires a clean Unity 6 host project to import the package and
run its EditMode and PlayMode tests.

## Documentation

- [Context / Network Boundary](Docs/ContextNetworkBoundary.md)
- [Module: Animation](Docs/Module-Animation.md)
- [Module: Camera](Docs/Module-Camera.md)
- [Module: Persistence](Docs/Module-Persistence.md)
- [Changelog](CHANGELOG.md)

Module documents describe transitional implementations unless they explicitly
state that a 0.4 contract is frozen.

## Versioning

- Integration branch: `dev/0.4.0`
- Work branches: `pre/NN-topic`
- UPM prereleases: `0.4.0-pre.N`
- 0.3.9 remains the historical runtime line; no migration runtime is bundled
  into 0.4.

## License

MIT
