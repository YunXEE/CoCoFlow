# CoCoFlow Documentation

This is the Unity Package Manager documentation entry point for CoCoFlow
`0.4.3`.

The mature Runtime surfaces are the Core Engine, Camera, Persistence, and UI.
Here, mature means stable public Runtime APIs, proven project use, and documented
boundaries—not maximum performance, complete Editor tooling, zero defects, or
marketplace certification. Map and Pooling are explicitly immature and do not
guarantee API, configuration, or serialized compatibility. Other modules are
unrated in this release. Explicit breaking changes are listed below and in the
[changelog](../CHANGELOG.md).

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

## State mailboxes and automatic binding

Host-local Intent sources are sampled each Tick. External struct events instead
pass through `ICoCoEventInboxAdapter<TEvent>.Accept(in payload)` into the Host's
typed inbox; a Tick may contain no mail or several messages. Reference Adapters
in the Host list and read the frozen batch with
`context.Inbox.TryGetBatch<TEvent>(out var mails)` and `mails.TryRead(index, out var mail)`.

`context.Outbox.Publish(in payload)` snapshots a struct event for publication
after the complete Tick commit. It needs no send Adapter or per-event Binding.
Reads do not consume mail; the next Tick replaces the batch. Running accepts
direct mail, while Suspended/Previewing drops it. Full queues drop new arrivals;
capacity zero retains no mail. Failed Temporal calls preserve pending mail.

Mark a State with `[CoCoState("Attack", ProvidesActionProgress = true)]` and call
`context.TrySetActionProgress(value)` to supply its normalized progress.
Mark each independent `CoCoStateCondition` evaluator class with `[Condition]`;
several classes may share one file. Add the desired condition in the Graph.
It evaluates a requested transition and does not request one itself.

See [StateGraph Runtime and Host](../Docs/StateGraphRuntime.md#direct-state-mailbox-d043-040711121415)
for complete examples and the existing legacy Event-to-Intent boundary.

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

LocomotionOperator and AnimAutoOperator support both interfaces. CameraOperator
supports save/load only. Non-Operator resource bindings keep their separate role;
Map and Pooling behavior is unchanged.

History capacity remains zero or at least two; zero still permits save/load.
A failed recovery keeps its original recovery purpose for world correction.
Correcting a normal Tick fault requires enabled recovery for every bound Operator.
Recovery does not expand Animator snapshot fidelity or add world rollback.

Locomotion sources now live under `Runtime/Modules/Locomotion`. Its pure C#
contracts remain in the separate `CoCoFlow.Runtime.Locomotion.Contracts` assembly,
in `Contracts/LocomotionContract.cs`; existing type names remain unchanged.

## StateLogic properties

Declare reads with `[Intent] public InputIntentData Input { get; set; }` and writes with
`[Operation] public ILocomotionSection Movement { get; set; }`.
Use `Movement.MoveZ = speed;` or `Movement.Jump(impulse);` inside the normal callback.
The framework refreshes Intent using the existing TryFirst rule and routes Section writes
through the existing composition and Operator execution. Bind the corresponding
Intent source and Operator on the Host. Module sources can provide their own
standard registration; see [Host startup](../Docs/StateGraphRuntime.md#standard-binding-startup).
The script template leaves both dependency declarations to the author.

## Config and Memory fields

On the standard binding path, declare public instance fields on StateLogic:

```csharp
[Config] public float Speed = 6f;
[Memory] public bool ImpulseSent;
```

Config values belong to each Graph State and are edited in the Graph Editor.
Missing overrides use the field initializer; zero and false are valid overrides.
Memory is captured with Context and restored with the saved activation state.
Both kinds of fields must be unmanaged. The runtime generic binding path has
not been verified in an IL2CPP Player.
