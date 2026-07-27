# Project Scaffold

> Pre14 contract: `0.4.0-pre.14` · Updated 2026-07-27

Open `CoCoFlow/Setup/Project Scaffold` directly or from Setup Assistant. The
default root is `Assets/CoCoFlowProject/`. The selected assembly mode applies
only to the Unity-facing Runtime layer: use regular Assembly-CSharp compilation
or generate `CoCoFlowProject.Runtime.asmdef`.

The Graph layer is always isolated in the generated
`CoCoFlowProject.Graph.asmdef` with `noEngineReferences: true`. It references
only the pure Contracts, StateFlow, and StateGraph assemblies. It never
references Unity, Input System, `InputRuntime`, or another gameplay module.

```text
Assets/CoCoFlowProject/
├── Graph/
│   ├── ProjectContractIds.cs
│   ├── ProjectIntent.cs
│   ├── ProjectStateLogic.cs
│   ├── ProjectOperationContracts.cs
│   └── CoCoFlowProject.Graph.asmdef
└── Runtime/
    ├── ProjectPlayerIntentSource.cs
    ├── ProjectContext.cs
    ├── ProjectContextSource.cs
    ├── ProjectOperations.cs
    ├── ProjectPersistence.cs
    ├── ProjectStateGraphBindings.cs
    └── ProjectInputBindingOverrideStore.cs
```

Custom-asmdef mode additionally creates
`Assets/CoCoFlowProject/CoCoFlowProject.Runtime.asmdef`.

## Safety transaction

Apply always follows one sequence:

1. Build a complete Preview of every target path.
2. Block if any target exists, a generated path crosses a symlink/junction, or
   multiple compiled project binding providers are found.
3. Ask for explicit confirmation.
4. Rebuild the Preview and compare its SHA-256 fingerprint. Any request,
   Provider, conflict, path, or generated-content change requires confirmation
   of a fresh Preview.
5. Stage every file under `Library/CoCoFlow/ProjectScaffold`.
6. Re-read and validate the staged C#/JSON and safe relative paths.
7. Recheck path safety and publish each target with `FileMode.CreateNew`.
8. If publishing fails, remove only files owned by that Apply and report any
   residual path that could not be removed.

The generator never overwrites a project file. A second Apply is blocked by the
existing targets.

## Provider behavior

When no `ICoCoStateGraphProjectBindingProvider` exists, the scaffold generates
the current Provider/install entry plus a runnable project Intent, State,
Operation Section, Operator, Context, Persistence, and Input override-store
starter. This is the current Host binding route, not a legacy StateGraph
controller path.

When exactly one provider exists, no second provider is generated. Preview
shows the concrete integration order: register the project Catalog before
Freeze, register runtime declarations before `TryBeginIntentBindings`, begin
Intent bindings after all project Intent declarations, then bind the Source,
Graph State slot, State factory, and Operation. Multiple providers block Apply.

The generated starter binding deliberately expects exactly one generated
`ProjectStateLogic` State in the Graph so that its Graph-owned activation state
slot has one unambiguous owner. Adding more project State instances is an
explicit project change: extend the Catalog and runtime state-slot bindings
instead of copying the starter binding unchanged.

In the scene, assign `ProjectPlayerIntentSource` to Host Intent Source slot 0
and `ProjectOperations` to Host Operators. The Source is the Unity-facing
adapter: it reads `InputRuntime`, converts `InputCommandBatch<T>` and `Vector2`
into pure project semantic values, and then exposes only `ProjectPlayerIntent`
to the Graph. These two scene steps are also shown in every full Preview.

Graph authoring and Player build validation still use the project-owned Editor
Catalog installation described in the StateGraph Editor contract. In a project
without an Editor Catalog aggregator, an Editor-only bootstrap can assign
`CoCoStateGraphEditorCatalogProvider.Provider` to
`ProjectStateGraphBindings.CreateCatalog`. If the project already aggregates
multiple Catalog contributors, add `ProjectStateGraphBindings.TryRegisterCatalog`
to that aggregation instead of replacing it. This Editor hook is separate from
the generated runtime Provider and is never referenced by the pure Graph or
Runtime assemblies.

The scaffold never generates a Root Context, Provider V2, second Host,
aggregate Context, or the obsolete `InputReader` route.
