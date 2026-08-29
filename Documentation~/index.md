# CoCoFlow Documentation

This is the Unity Package Manager documentation entry point for CoCoFlow
`0.4.0`.

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
