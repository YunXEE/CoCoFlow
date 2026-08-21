# CoCoFlow Dependency Matrix

> **Derived snapshot** regenerated at `99574a0 + triage fix round (this commit)`. The single source of truth is
> `package.json` + asmdef + source; this document only explains and is refreshed
> per delivery. The boundary guard test (`CoCoDependencyBoundaryGuardTests`) is the
> executable gate.

## Hard dependencies (package.json)

- `com.unity.inputsystem` 1.18.0
- `com.unity.localization` 1.5.9
- `com.unity.cinemachine` 3.1.6
- `com.unity.ai.navigation` 2.0.0
- `com.unity.mathematics` 1.3.3
- `com.unity.nuget.newtonsoft-json` 3.2.2
- `com.unity.splines` 2.6.0

### Transitive behavior (F-F precise wording, supersedes earlier snapshot)

- `com.unity.localization` hard-requires `com.unity.addressables`; Unity resolves it to **≥1.25.0
  (2.9.1 observed in our hosts)**. A host without any Addressables assembly is therefore not a
  supported state while Localization stays a hard dependency. The `[2.9.1,3.0.0)` versionDefines
  triple gates on the **resolved version** (backend compiles only when the range matches), not on
  mere presence.
- Test assemblies use the official isolation (InputSystem-style): `optionalUnityReferences:
  [TestAssemblies]` + `UNITY_INCLUDE_TESTS` defineConstraint, **no explicit testrunner
  references**; discovery requires host-side activation (`testables: [com.yunxee.cocoflow]` in
  manifest, or existing test-host state). This keeps consumer Player builds clean (VR-1 E3)
  while clean test hosts discover everything (VR-1 E1/E2).

### Known accepted boundary (F-H, user decision A 2026-08-21)

`COCOFLOW_UNITASK_SUPPORT` serves both versionDefines injection (UPM path) and manual
ScriptingDefineSymbols (unitypackage path, which has no version discoverable by design).
A stale manual define combined with an out-of-range UPM version could theoretically bypass the
version gate. Accepted as-is: single-maintainer project; the Setup Assistant removes stale
defines on Apply whenever the UPM form is registered. Not a supported bypass path.

## External assembly consumers (by dependency)

- **DOTween.Modules** ×1: CoCoFlow.Runtime.Modules.UI
- **GUID:79ea9195d41d4b2492e4445155765e98** ×1: CoCoFlow.Editor.Core
- **GUID:7c83763f49c24070b1851eb60db08251** ×1: CoCoFlow.Editor.Modules.Animation
- **GUID:ae406d5376c94b98b9ec1dcd5d061bf9** ×1: CoCoFlow.Editor.Modules.Animation
- **GUID:dc788f1e652d49c38cd59ca776d48ed8** ×1: CoCoFlow.Editor.Modules.Persistence
- **GUID:ee5693ffef7949f8b9effa78b29b02b0** ×1: CoCoFlow.Editor.Modules.UI
- **UniTask** ×26: CoCoFlow.Fixtures.ExternalMapTa, CoCoFlow.Runtime.Content, CoCoFlow.Runtime.Content.Addressables, CoCoFlow.Runtime.Modules.Animation.UniTask, CoCoFlow.Runtime.Modules.Map, CoCoFlow.Runtime.Modules.Map.Pooling, CoCoFlow.Runtime.Modules.Map.Temporal, CoCoFlow.Runtime.Modules.UI, CoCoFlow.Runtime.Pooling, CoCoFlow.Tests.Editor.Content, CoCoFlow.Tests.Editor.Content.Addressables, CoCoFlow.Tests.Editor.Map, CoCoFlow.Tests.Editor.Map.Authoring, CoCoFlow.Tests.Editor.Map.Pooling, CoCoFlow.Tests.Editor.Pooling, CoCoFlow.Tests.Runtime.Content.Addressables, CoCoFlow.Tests.Runtime.Content.DirectScene, CoCoFlow.Tests.Runtime.ContentConsumers, CoCoFlow.Tests.Runtime.Map, CoCoFlow.Tests.Runtime.Map.Addressables, CoCoFlow.Tests.Runtime.Map.DirectScene, CoCoFlow.Tests.Runtime.Map.Pooling, CoCoFlow.Tests.Runtime.Map.PublicSdk, CoCoFlow.Tests.Runtime.Map.Temporal, CoCoFlow.Tests.Runtime.Pooling, CoCoFlow.Tests.Runtime.Pooling.Temporal
- **UniTask.DOTween** ×1: CoCoFlow.Runtime.Modules.UI
- **Unity.Addressables** ×4: CoCoFlow.Runtime.Content.Addressables, CoCoFlow.Tests.Editor.Content.Addressables, CoCoFlow.Tests.Runtime.Content.Addressables, CoCoFlow.Tests.Runtime.Map.Addressables
- **Unity.Addressables.Editor** ×1: CoCoFlow.Tests.Editor.Content.Addressables
- **Unity.Cinemachine** ×3: CoCoFlow.Runtime.Modules.Camera, CoCoFlow.Samples.Gameplay.Tests.Runtime, CoCoFlow.Tests.Runtime.ContextLifecycle
- **Unity.InputSystem** ×9: CoCoFlow.Runtime.Gameplay.Character, CoCoFlow.Runtime.Modules.Camera, CoCoFlow.Runtime.Modules.Input, CoCoFlow.Runtime.Modules.Input.UI, CoCoFlow.Runtime.Modules.UI, CoCoFlow.Samples.Gameplay.Tests.Runtime, CoCoFlow.Tests.Editor.Input, CoCoFlow.Tests.Runtime.ContextLifecycle, CoCoFlow.Tests.Runtime.Input
- **Unity.InputSystem.TestFramework** ×3: CoCoFlow.Samples.Gameplay.Tests.Runtime, CoCoFlow.Tests.Runtime.ContextLifecycle, CoCoFlow.Tests.Runtime.Input
- **Unity.Localization** ×3: CoCoFlow.Runtime.Modules.Localization, CoCoFlow.Runtime.Modules.Localization.UI, CoCoFlow.Tests.Runtime.Localization
- **Unity.Mathematics** ×4: CoCoFlow.Editor.Gameplay.Enemy, CoCoFlow.Runtime.Gameplay.Enemy, CoCoFlow.Samples.Gameplay.Tests.Runtime, CoCoFlow.Tests.Editor.Enemy
- **Unity.ResourceManager** ×6: CoCoFlow.Runtime.Content.Addressables, CoCoFlow.Runtime.Modules.Localization.UI, CoCoFlow.Tests.Editor.Content.Addressables, CoCoFlow.Tests.Runtime.Content.Addressables, CoCoFlow.Tests.Runtime.Localization, CoCoFlow.Tests.Runtime.Map.Addressables
- **Unity.Splines** ×4: CoCoFlow.Editor.Gameplay.Enemy, CoCoFlow.Runtime.Gameplay.Enemy, CoCoFlow.Samples.Gameplay.Tests.Runtime, CoCoFlow.Tests.Editor.Enemy
- **Unity.TextMeshPro** ×3: CoCoFlow.Runtime.Modules.Localization.UI, CoCoFlow.Runtime.Modules.UI, CoCoFlow.Tests.Runtime.Localization

## Optional dependency authority

| Dependency | Mechanism | Authority |
|---|---|---|
| UniTask ≥2.5.11 <3.0.0 | asmdef versionDefines (exact triple) + retained defineConstraints | UPM resolved version; Setup Assistant removes stale manual defines |
| Addressables ≥2.9.1 <3.0.0 | asmdef versionDefines (backend assemblies only) | UPM resolved version |
| DOTween (unitypackage) | manual ScriptingDefineSymbols via Setup Assistant | assembly presence |
| Cinemachine 3.1.6 | hard dependency, unconstrained reference | package.json |

## Full asmdef matrix

| Asmdef | External refs | defineConstraints | versionDefines |
|---|---|---|---|
| `Editor/Content/CoCoFlow.Editor.Content.asmdef` | — | COCOFLOW_UNITASK_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Editor/Core/CoCoFlow.Editor.Core.asmdef` | GUID:79ea9195d41d4b2492e4445155765e98 | — | — |
| `Editor/Modules/Animation/CoCoFlow.Editor.Modules.Animation.asmdef` | GUID:7c83763f49c24070b1851eb60db08251, GUID:ae406d5376c94b98b9ec1dcd5d061bf9 | — | — |
| `Editor/Modules/Map/CoCoFlow.Editor.Modules.Map.asmdef` | — | COCOFLOW_UNITASK_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Editor/Modules/Persistence/CoCoFlow.Editor.Modules.Persistence.asmdef` | GUID:dc788f1e652d49c38cd59ca776d48ed8 | — | — |
| `Editor/Modules/UI/CoCoFlow.Editor.Modules.UI.asmdef` | GUID:ee5693ffef7949f8b9effa78b29b02b0 | COCOFLOW_UNITASK_SUPPORT, COCOFLOW_DOTWEEN_SUPPORT, UNITASK_DOTWEEN_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Editor/Pooling/CoCoFlow.Editor.Pooling.asmdef` | — | COCOFLOW_UNITASK_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Editor/ProjectScaffold/CoCoFlow.Editor.ProjectScaffold.asmdef` | — | — | — |
| `Editor/StateGraph/CoCoFlow.Editor.StateGraph.asmdef` | — | — | — |
| `Editor/StateGraph/PlayerMetadata/CoCoFlow.Editor.StateGraph.PlayerMetadata.asmdef` | — | — | — |
| `Editor/StateGraphHost/CoCoFlow.Editor.StateGraphHost.asmdef` | — | — | — |
| `Runtime/Animation/Contracts/CoCoFlow.Runtime.Animation.Contracts.asmdef` | — | — | — |
| `Runtime/Content/Addressables/CoCoFlow.Runtime.Content.Addressables.asmdef` | Unity.Addressables, Unity.ResourceManager, UniTask | COCOFLOW_UNITASK_SUPPORT, COCOFLOW_ADDRESSABLES_2_9_OR_NEWER | com.unity.addressables [2.9.1,3.0.0)→COCOFLOW_ADDRESSABLES_2_9_OR_NEWER; com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Runtime/Content/CoCoFlow.Runtime.Content.asmdef` | UniTask | COCOFLOW_UNITASK_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Runtime/Core/CoCoFlow.Runtime.Core.asmdef` | — | — | — |
| `Runtime/Core/Contracts/CoCoFlow.Runtime.Core.Contracts.asmdef` | — | — | — |
| `Runtime/Core/StateFlow/CoCoFlow.Runtime.Core.StateFlow.asmdef` | — | — | — |
| `Runtime/Core/StateGraph/CoCoFlow.Runtime.Core.StateGraph.asmdef` | — | — | — |
| `Runtime/Core/StateGraphAuthoring/CoCoFlow.Runtime.Core.StateGraphAuthoring.asmdef` | — | — | — |
| `Runtime/Modules/Animation/CoCoFlow.Runtime.Modules.Animation.asmdef` | — | — | — |
| `Runtime/Modules/Animation/DOTween/CoCoFlow.Runtime.Modules.Animation.DOTween.asmdef` | — | COCOFLOW_DOTWEEN_SUPPORT | — |
| `Runtime/Modules/Animation/UniTask/CoCoFlow.Runtime.Modules.Animation.UniTask.asmdef` | UniTask | COCOFLOW_UNITASK_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Runtime/Modules/Camera/CoCoFlow.Runtime.Modules.Camera.asmdef` | Unity.Cinemachine, Unity.InputSystem | — | — |
| `Runtime/Modules/Input/CoCoFlow.Runtime.Modules.Input.asmdef` | Unity.InputSystem | — | — |
| `Runtime/Modules/Input/UI/CoCoFlow.Runtime.Modules.Input.UI.asmdef` | Unity.InputSystem | COCOFLOW_UNITASK_SUPPORT, COCOFLOW_DOTWEEN_SUPPORT, UNITASK_DOTWEEN_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Runtime/Modules/Localization/CoCoFlow.Runtime.Modules.Localization.asmdef` | Unity.Localization | — | — |
| `Runtime/Modules/Localization/UI/CoCoFlow.Runtime.Modules.Localization.UI.asmdef` | Unity.Localization, Unity.ResourceManager, Unity.TextMeshPro | COCOFLOW_UNITASK_SUPPORT, COCOFLOW_DOTWEEN_SUPPORT, UNITASK_DOTWEEN_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Runtime/Modules/Map/CoCoFlow.Runtime.Modules.Map.asmdef` | UniTask | COCOFLOW_UNITASK_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Runtime/Modules/Map/Pooling/CoCoFlow.Runtime.Modules.Map.Pooling.asmdef` | UniTask | COCOFLOW_UNITASK_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Runtime/Modules/Map/Temporal/CoCoFlow.Runtime.Modules.Map.Temporal.asmdef` | UniTask | COCOFLOW_UNITASK_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Runtime/Modules/Persistence/CoCoFlow.Runtime.Modules.Persistence.asmdef` | — | — | — |
| `Runtime/Modules/UI/CoCoFlow.Runtime.Modules.UI.asmdef` | DOTween.Modules, UniTask, UniTask.DOTween, Unity.TextMeshPro, Unity.InputSystem | COCOFLOW_UNITASK_SUPPORT, COCOFLOW_DOTWEEN_SUPPORT, UNITASK_DOTWEEN_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Runtime/Pooling/CoCoFlow.Runtime.Pooling.asmdef` | UniTask | COCOFLOW_UNITASK_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Runtime/Pooling/Temporal/CoCoFlow.Runtime.Pooling.Temporal.asmdef` | — | COCOFLOW_UNITASK_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Runtime/StateGraphHost/CoCoFlow.Runtime.StateGraphHost.asmdef` | — | — | — |
| `Samples~/Gameplay/Character/CoCoFlow.Runtime.Gameplay.Character.asmdef` | Unity.InputSystem | — | — |
| `Samples~/Gameplay/Editor/Enemy/CoCoFlow.Editor.Gameplay.Enemy.asmdef` | Unity.Splines, Unity.Mathematics | — | — |
| `Samples~/Gameplay/Enemy/CoCoFlow.Runtime.Gameplay.Enemy.asmdef` | Unity.Splines, Unity.Mathematics | — | — |
| `Samples~/Gameplay/Item/CoCoFlow.Runtime.Gameplay.Item.asmdef` | — | — | — |
| `Samples~/Gameplay/Tests/Editor/CoCoFlow.Tests.Editor.Enemy.asmdef` | Unity.Mathematics, Unity.Splines | UNITY_INCLUDE_TESTS | — |
| `Samples~/Gameplay/Tests/Runtime/CoCoFlow.Samples.Gameplay.Tests.Runtime.asmdef` | Unity.Cinemachine, Unity.InputSystem, Unity.InputSystem.TestFramework, Unity.Mathematics, Unity.Splines | UNITY_INCLUDE_TESTS | — |
| `Tests/Editor/Content/Addressables/CoCoFlow.Tests.Editor.Content.Addressables.asmdef` | UniTask, Unity.Addressables, Unity.Addressables.Editor, Unity.ResourceManager | COCOFLOW_ADDRESSABLES_2_9_OR_NEWER, COCOFLOW_UNITASK_SUPPORT, UNITY_INCLUDE_TESTS | com.unity.addressables [2.9.1,3.0.0)→COCOFLOW_ADDRESSABLES_2_9_OR_NEWER; com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Editor/Content/CoCoFlow.Tests.Editor.Content.asmdef` | UniTask | COCOFLOW_UNITASK_SUPPORT, UNITY_INCLUDE_TESTS | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Editor/Core/CoCoFlow.Tests.Editor.Core.asmdef` | — | UNITY_INCLUDE_TESTS | — |
| `Tests/Editor/CoreContracts/CoCoFlow.Tests.Editor.CoreContracts.asmdef` | — | UNITY_INCLUDE_TESTS | — |
| `Tests/Editor/Input/CoCoFlow.Tests.Editor.Input.asmdef` | Unity.InputSystem | UNITY_INCLUDE_TESTS | — |
| `Tests/Editor/Map/Authoring/CoCoFlow.Tests.Editor.Map.Authoring.asmdef` | UniTask | COCOFLOW_UNITASK_SUPPORT, UNITY_INCLUDE_TESTS | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Editor/Map/CoCoFlow.Tests.Editor.Map.asmdef` | UniTask | COCOFLOW_UNITASK_SUPPORT, UNITY_INCLUDE_TESTS | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Editor/Map/Pooling/CoCoFlow.Tests.Editor.Map.Pooling.asmdef` | UniTask | COCOFLOW_UNITASK_SUPPORT, UNITY_INCLUDE_TESTS | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Editor/Pooling/CoCoFlow.Tests.Editor.Pooling.asmdef` | UniTask | COCOFLOW_UNITASK_SUPPORT, UNITY_INCLUDE_TESTS | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Editor/ProjectScaffold/CoCoFlow.Tests.Editor.ProjectScaffold.asmdef` | — | UNITY_INCLUDE_TESTS | — |
| `Tests/Editor/StateGraph/AuthoringDependencyFixtures/CoCoFlow.Tests.StateGraphAuthoringDependencyFixtures.asmdef` | — | UNITY_INCLUDE_TESTS | — |
| `Tests/Editor/StateGraph/CoCoFlow.Tests.Editor.StateGraph.asmdef` | — | UNITY_INCLUDE_TESTS | — |
| `Tests/Editor/StateGraph/Fixtures/CoCoFlow.Tests.StateGraphAuthoringFixtures.asmdef` | — | UNITY_INCLUDE_TESTS | — |
| `Tests/Editor/StateGraph/LegacyCoreDependencyFixtures/CoCoFlow.Tests.StateGraphLegacyCoreDependencyFixtures.asmdef` | — | UNITY_INCLUDE_TESTS | — |
| `Tests/Editor/StateGraph/PlayerMetadataFixtures/Sections/CoCoFlow.Tests.StateGraphPlayerMetadataSections.asmdef` | — | UNITY_INCLUDE_TESTS | — |
| `Tests/Editor/StateGraph/PlayerMetadataFixtures/Values/CoCoFlow.Tests.StateGraphPlayerMetadataValues.asmdef` | — | UNITY_INCLUDE_TESTS | — |
| `Tests/Editor/StateGraph/TransitiveDependencyFixtures/Author/CoCoFlow.Tests.StateGraphTransitiveDependencyAuthor.asmdef` | — | UNITY_INCLUDE_TESTS | — |
| `Tests/Editor/StateGraph/TransitiveDependencyFixtures/Helper/CoCoFlow.Tests.StateGraphTransitiveDependencyHelper.asmdef` | — | UNITY_INCLUDE_TESTS | — |
| `Tests/Editor/StateGraphHost/CoCoFlow.Tests.Editor.StateGraphHost.asmdef` | — | UNITY_INCLUDE_TESTS | — |
| `Tests/Fixtures/MapExternalTa/Runtime/CoCoFlow.Fixtures.ExternalMapTa.asmdef` | UniTask | COCOFLOW_UNITASK_SUPPORT, UNITY_INCLUDE_TESTS | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Runtime/Animation/CoCoFlow.Tests.Runtime.Animation.ReplaySpike.asmdef` | — | UNITY_INCLUDE_TESTS | — |
| `Tests/Runtime/Animation/Contracts/CoCoFlow.Tests.Runtime.Animation.asmdef` | — | UNITY_INCLUDE_TESTS | — |
| `Tests/Runtime/Animation/DOTween/CoCoFlow.Tests.Runtime.Animation.DOTween.asmdef` | — | COCOFLOW_DOTWEEN_SUPPORT, UNITY_INCLUDE_TESTS | — |
| `Tests/Runtime/CoCoFlow.Tests.Runtime.ContextLifecycle.asmdef` | Unity.Cinemachine, Unity.InputSystem, Unity.InputSystem.TestFramework | UNITY_INCLUDE_TESTS | — |
| `Tests/Runtime/Content/Addressables/CoCoFlow.Tests.Runtime.Content.Addressables.asmdef` | UniTask, Unity.Addressables, Unity.ResourceManager | COCOFLOW_ADDRESSABLES_2_9_OR_NEWER, COCOFLOW_UNITASK_SUPPORT, UNITY_INCLUDE_TESTS | com.unity.addressables [2.9.1,3.0.0)→COCOFLOW_ADDRESSABLES_2_9_OR_NEWER; com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Runtime/Content/CoCoFlow.Tests.Runtime.ContentConsumers.asmdef` | UniTask | COCOFLOW_DOTWEEN_SUPPORT, COCOFLOW_UNITASK_SUPPORT, UNITASK_DOTWEEN_SUPPORT, UNITY_INCLUDE_TESTS | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Runtime/Content/DirectScene/CoCoFlow.Tests.Runtime.Content.DirectScene.asmdef` | UniTask | COCOFLOW_UNITASK_SUPPORT, UNITY_INCLUDE_TESTS | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Runtime/Input/CoCoFlow.Tests.Runtime.Input.asmdef` | Unity.InputSystem, Unity.InputSystem.TestFramework | UNITY_INCLUDE_TESTS | — |
| `Tests/Runtime/Localization/CoCoFlow.Tests.Runtime.Localization.asmdef` | Unity.Localization, Unity.ResourceManager, Unity.TextMeshPro | COCOFLOW_DOTWEEN_SUPPORT, COCOFLOW_UNITASK_SUPPORT, UNITASK_DOTWEEN_SUPPORT, UNITY_INCLUDE_TESTS | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Runtime/Map/Addressables/CoCoFlow.Tests.Runtime.Map.Addressables.asmdef` | UniTask, Unity.Addressables, Unity.ResourceManager | COCOFLOW_ADDRESSABLES_2_9_OR_NEWER, COCOFLOW_UNITASK_SUPPORT, UNITY_INCLUDE_TESTS | com.unity.addressables [2.9.1,3.0.0)→COCOFLOW_ADDRESSABLES_2_9_OR_NEWER; com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Runtime/Map/CoCoFlow.Tests.Runtime.Map.asmdef` | UniTask | COCOFLOW_UNITASK_SUPPORT, UNITY_INCLUDE_TESTS | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Runtime/Map/DirectScene/CoCoFlow.Tests.Runtime.Map.DirectScene.asmdef` | UniTask | COCOFLOW_UNITASK_SUPPORT, UNITY_INCLUDE_TESTS | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Runtime/Map/Pooling/CoCoFlow.Tests.Runtime.Map.Pooling.asmdef` | UniTask | COCOFLOW_UNITASK_SUPPORT, UNITY_INCLUDE_TESTS | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Runtime/Map/PublicSdk/CoCoFlow.Tests.Runtime.Map.PublicSdk.asmdef` | UniTask | COCOFLOW_UNITASK_SUPPORT, UNITY_INCLUDE_TESTS | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Runtime/Map/Temporal/CoCoFlow.Tests.Runtime.Map.Temporal.asmdef` | UniTask | COCOFLOW_UNITASK_SUPPORT, UNITY_INCLUDE_TESTS | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Runtime/Pooling/CoCoFlow.Tests.Runtime.Pooling.asmdef` | UniTask | COCOFLOW_UNITASK_SUPPORT, UNITY_INCLUDE_TESTS | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Runtime/Pooling/Temporal/CoCoFlow.Tests.Runtime.Pooling.Temporal.asmdef` | UniTask | COCOFLOW_UNITASK_SUPPORT, UNITY_INCLUDE_TESTS | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Runtime/StateGraphHost/CoCoFlow.Tests.Runtime.StateGraphHost.asmdef` | — | UNITY_INCLUDE_TESTS | — |
| `Tests/Runtime/StateGraphHost/Fixtures/CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures.asmdef` | — | UNITY_INCLUDE_TESTS | — |
