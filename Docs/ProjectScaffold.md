# Project Scaffold

> Pre14 contract: `0.4.0-pre.14` · Updated 2026-07-27

Open `CoCoFlow/Setup/Project Scaffold` directly or from Setup Assistant. The
default root is `Assets/CoCoFlowProject/`. Choose regular Assembly-CSharp
compilation or generate `CoCoFlowProject.Runtime.asmdef`.

## Safety transaction

Apply always follows one sequence:

1. Build a complete Preview of every target path.
2. Block if any target exists or multiple project binding providers are found.
3. Ask for explicit confirmation.
4. Stage every file under `Library/CoCoFlow/ProjectScaffold`.
5. Re-read and validate the staged C#/JSON and safe relative paths.
6. Publish each target with `FileMode.CreateNew`.
7. If publishing fails, remove only files created by that Apply.

The generator never overwrites a project file. A second Apply is blocked by the
existing targets.

## Provider behavior

When no `ICoCoStateGraphProjectBindingProvider` exists, the scaffold generates
the current provider/install entry plus project Intent, Context, State logic,
Operation, Persistence, and Input override-store candidates. The generated
provider is intentionally a compile-safe starter: project descriptor and Host
bindings must be registered before running a non-empty graph.

When exactly one provider exists, no second provider is generated. Preview
shows the concrete source/operation integration guidance for that provider.
Multiple providers block Apply.

The scaffold never generates a Root Context, Provider V2, second Host,
aggregate Context, or the obsolete `InputReader` route.
