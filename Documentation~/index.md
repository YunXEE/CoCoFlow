# CoCoFlow Documentation

This is the Unity Package Manager documentation entry point for CoCoFlow
`0.4.1`.

The mature Runtime surfaces are the Core Engine, Camera, Persistence, and UI.
Here, mature means stable public Runtime APIs, proven project use, and documented
boundaries—not maximum performance, complete Editor tooling, zero defects, or
marketplace certification. Map and Pooling are explicitly immature and do not
guarantee API, configuration, or serialized compatibility. Other modules are
unrated in this release.

## Start here

- [Package overview](../README.md)
- [简体中文概览](../README.zh-CN.md)
- [Changelog](../CHANGELOG.md)
- [Dependency matrix](../Docs/DependencyMatrix.md)

## Core Engine

- [StateGraph Asset and Compiler](../Docs/StateGraphCompiler.md)
- [StateGraph Runtime and Host](../Docs/StateGraphRuntime.md)
- [State Flow / Event Boundary](../Docs/ContextNetworkBoundary.md)
- [Temporal Rewind](../Docs/TemporalRewind.md)
- [StateGraph Editor and Runtime Debugger](../Docs/StateGraphEditor.md)

The StateGraph Editor page describes current tooling; Editor behavior is not
part of the Runtime API maturity guarantee. Older EventBus, Services, and
Context facilities under `Runtime/Core/*.cs` are also outside the Core Engine
maturity statement.

## Modules

- [Camera](../Docs/Module-Camera.md) — mature
- [Persistence](../Docs/Module-Persistence.md) — mature
- [UI](../Docs/Module-UI.md) — mature, with documented efficiency limits
- [Map Region Fidelity](../Docs/Module-Map.md) — immature
- [Object Pooling](../Docs/ObjectPooling.md) — immature
- [Content acquisition and ownership](../Docs/ContentOwnership.md)
- [Input](../Docs/Module-Input.md)
- [Localization](../Docs/Module-Localization.md)
- [Animation](../Docs/Module-Animation.md)

## Operator recovery

In the Host's Operators list, each component exposes only the recovery switches
supported by its interfaces: `ICoCoPersistenceRestoreOperator` for save/load and
`ICoCoTemporalRestoreOperator` for history. Both switches start off and are independent.
Configure them before starting the Host; running Hosts keep their startup settings.
Reordering the list keeps settings with the component, not the row number.

Implement `TryRestore(in CoCoContextRestoreBindingContext, out CoCoDiagnostic)`
and read committed state through `context.Reader` during that callback only.
The Host dispatches enabled Operators in binding order. Normal execution and
Context capture continue whether recovery is enabled or disabled.

Disabled or unsupported recovery retains that Operator's current Outcome slots
(layout defaults before the first commit). Graph/Memory restore from the target
snapshot, then Derived values rebuild from those combined inputs. Preview keeps
the current committed frame as its reference until confirmation.

LocomotionOperator and AnimAutoOperator support both interfaces. Old Operator
Restore Binding chains are no longer supported: remove those references and
enable the Host switches explicitly. Non-Operator resource bindings keep their
existing separate role; Map and Pooling behavior is unchanged.

History capacity remains zero or at least two; zero still permits save/load.
A failed recovery keeps its original recovery purpose for world correction.
Correcting a normal Tick fault requires enabled recovery for every bound Operator.
Recovery does not expand Animator snapshot fidelity or add world rollback.

Locomotion sources now live under `Runtime/Modules/Locomotion`. Its pure C#
contracts remain in the separate `CoCoFlow.Runtime.Locomotion.Contracts` assembly,
in `Contracts/LocomotionContract.cs`; existing type names remain unchanged.

## StateLogic properties

Declare reads with `[Intent] public RawInputIntent Input { get; set; }` and writes with
`[Operation] public ILocomotionSection Movement { get; set; }`.
Use `Movement.MoveZ = speed;` or `Movement.Jump(impulse);` inside the normal callback.
The framework refreshes Intent using the existing TryFirst rule and routes Section writes
through the existing composition and Operator execution. No source registration is added.
Class-level dependency attributes and explicit Providers remain supported; choose one
style per Logic. The script template leaves both dependency declarations to the author.
