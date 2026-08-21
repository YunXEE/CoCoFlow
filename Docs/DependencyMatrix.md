# CoCoFlow Dependency Matrix

> **Derived snapshot** of Final Head `40ff49d` (working tree at generation time).
> The single source of truth is `package.json` + asmdef + source; this document
> only explains and is refreshed per release. The boundary guard test
> (`CoCoDependencyBoundaryGuardTests`) is the executable gate.

## Hard dependencies (package.json)

- `com.unity.inputsystem` 1.18.0
- `com.unity.localization` 1.5.9
- `com.unity.cinemachine` 3.1.6
- `com.unity.ai.navigation` 2.0.0
- `com.unity.mathematics` 1.3.3
- `com.unity.nuget.newtonsoft-json` 3.2.2
- `com.unity.splines` 2.6.0

### Transitive guarantees (observed at {HEAD})

- `Unity.ResourceManager` is always present via `com.unity.localization` → `com.unity.addressables` (hard chain).
  Consumers like `Runtime.Modules.Localization.UI` therefore must NOT carry the optional-backend versionDefines triple.
- `com.unity.addressables` itself resolves transitively at **2.9.1** in every host (localization chain), so the
  'Addressables absent' state does not exist in practice; the `[2.9.1,3.0.0)` versionDefines range gates on
  resolved version, and the optional backend activates wherever the range matches. Combo semantics documented
  in the PR15.10 delivery report.
- Test assemblies declare explicit `UnityEditor.TestRunner`/`UnityEngine.TestRunner` references (classification),
  empty-of-UNITY_INCLUDE_TESTS constraints, and rely on test-assembly classification for player exclusion
  (Unity's own pattern; batch clean hosts never re-evaluate UNITY_INCLUDE_TESTS constraints after import).

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
- **UnityEditor.TestRunner** ×20: CoCoFlow.Tests.Editor.Content, CoCoFlow.Tests.Editor.Content.Addressables, CoCoFlow.Tests.Editor.Core, CoCoFlow.Tests.Editor.CoreContracts, CoCoFlow.Tests.Editor.Enemy, CoCoFlow.Tests.Editor.Input, CoCoFlow.Tests.Editor.Map, CoCoFlow.Tests.Editor.Map.Authoring, CoCoFlow.Tests.Editor.Map.Pooling, CoCoFlow.Tests.Editor.Pooling, CoCoFlow.Tests.Editor.ProjectScaffold, CoCoFlow.Tests.Editor.StateGraph, CoCoFlow.Tests.Editor.StateGraphHost, CoCoFlow.Tests.StateGraphAuthoringDependencyFixtures, CoCoFlow.Tests.StateGraphAuthoringFixtures, CoCoFlow.Tests.StateGraphLegacyCoreDependencyFixtures, CoCoFlow.Tests.StateGraphPlayerMetadataSections, CoCoFlow.Tests.StateGraphPlayerMetadataValues, CoCoFlow.Tests.StateGraphTransitiveDependencyAuthor, CoCoFlow.Tests.StateGraphTransitiveDependencyHelper
- **UnityEngine.TestRunner** ×41: CoCoFlow.Fixtures.ExternalMapTa, CoCoFlow.Samples.Gameplay.Tests.Runtime, CoCoFlow.Tests.Editor.Content, CoCoFlow.Tests.Editor.Content.Addressables, CoCoFlow.Tests.Editor.Core, CoCoFlow.Tests.Editor.CoreContracts, CoCoFlow.Tests.Editor.Enemy, CoCoFlow.Tests.Editor.Input, CoCoFlow.Tests.Editor.Map, CoCoFlow.Tests.Editor.Map.Authoring, CoCoFlow.Tests.Editor.Map.Pooling, CoCoFlow.Tests.Editor.Pooling, CoCoFlow.Tests.Editor.ProjectScaffold, CoCoFlow.Tests.Editor.StateGraph, CoCoFlow.Tests.Editor.StateGraphHost, CoCoFlow.Tests.Runtime.Animation, CoCoFlow.Tests.Runtime.Animation.DOTween, CoCoFlow.Tests.Runtime.Animation.ReplaySpike, CoCoFlow.Tests.Runtime.Content.Addressables, CoCoFlow.Tests.Runtime.Content.DirectScene, CoCoFlow.Tests.Runtime.ContentConsumers, CoCoFlow.Tests.Runtime.ContextLifecycle, CoCoFlow.Tests.Runtime.Input, CoCoFlow.Tests.Runtime.Localization, CoCoFlow.Tests.Runtime.Map, CoCoFlow.Tests.Runtime.Map.Addressables, CoCoFlow.Tests.Runtime.Map.DirectScene, CoCoFlow.Tests.Runtime.Map.Pooling, CoCoFlow.Tests.Runtime.Map.PublicSdk, CoCoFlow.Tests.Runtime.Map.Temporal, CoCoFlow.Tests.Runtime.Pooling, CoCoFlow.Tests.Runtime.Pooling.Temporal, CoCoFlow.Tests.Runtime.StateGraphHost, CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures, CoCoFlow.Tests.StateGraphAuthoringDependencyFixtures, CoCoFlow.Tests.StateGraphAuthoringFixtures, CoCoFlow.Tests.StateGraphLegacyCoreDependencyFixtures, CoCoFlow.Tests.StateGraphPlayerMetadataSections, CoCoFlow.Tests.StateGraphPlayerMetadataValues, CoCoFlow.Tests.StateGraphTransitiveDependencyAuthor, CoCoFlow.Tests.StateGraphTransitiveDependencyHelper

## Optional dependency authority

| Dependency | Mechanism | Authority |
|---|---|---|
| UniTask ≥2.5.11 <3.0.0 | asmdef versionDefines (exact triple) + retained defineConstraints | UPM resolved version; Setup Assistant removes stale manual defines |
| Addressables ≥2.9.1 <3.0.0 | asmdef versionDefines (backend assemblies only) | UPM resolved version |
| DOTween (unitypackage) | manual ScriptingDefineSymbols via Setup Assistant | assembly presence |
| Cinemachine 3.1.6 | hard dependency, unconstrained | package.json |

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
| `Samples~/Gameplay/Tests/Editor/CoCoFlow.Tests.Editor.Enemy.asmdef` | Unity.Mathematics, Unity.Splines, UnityEditor.TestRunner, UnityEngine.TestRunner | — | — |
| `Samples~/Gameplay/Tests/Runtime/CoCoFlow.Samples.Gameplay.Tests.Runtime.asmdef` | Unity.Cinemachine, Unity.InputSystem, Unity.InputSystem.TestFramework, Unity.Mathematics, Unity.Splines, UnityEngine.TestRunner | — | — |
| `Tests/Editor/Content/Addressables/CoCoFlow.Tests.Editor.Content.Addressables.asmdef` | UniTask, Unity.Addressables, Unity.Addressables.Editor, Unity.ResourceManager, UnityEditor.TestRunner, UnityEngine.TestRunner | COCOFLOW_UNITASK_SUPPORT, COCOFLOW_ADDRESSABLES_2_9_OR_NEWER | com.unity.addressables [2.9.1,3.0.0)→COCOFLOW_ADDRESSABLES_2_9_OR_NEWER; com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Editor/Content/CoCoFlow.Tests.Editor.Content.asmdef` | UniTask, UnityEditor.TestRunner, UnityEngine.TestRunner | COCOFLOW_UNITASK_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Editor/Core/CoCoFlow.Tests.Editor.Core.asmdef` | UnityEditor.TestRunner, UnityEngine.TestRunner | — | — |
| `Tests/Editor/CoreContracts/CoCoFlow.Tests.Editor.CoreContracts.asmdef` | UnityEditor.TestRunner, UnityEngine.TestRunner | — | — |
| `Tests/Editor/Input/CoCoFlow.Tests.Editor.Input.asmdef` | Unity.InputSystem, UnityEditor.TestRunner, UnityEngine.TestRunner | — | — |
| `Tests/Editor/Map/Authoring/CoCoFlow.Tests.Editor.Map.Authoring.asmdef` | UniTask, UnityEditor.TestRunner, UnityEngine.TestRunner | COCOFLOW_UNITASK_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Editor/Map/CoCoFlow.Tests.Editor.Map.asmdef` | UniTask, UnityEditor.TestRunner, UnityEngine.TestRunner | COCOFLOW_UNITASK_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Editor/Map/Pooling/CoCoFlow.Tests.Editor.Map.Pooling.asmdef` | UniTask, UnityEditor.TestRunner, UnityEngine.TestRunner | COCOFLOW_UNITASK_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Editor/Pooling/CoCoFlow.Tests.Editor.Pooling.asmdef` | UniTask, UnityEditor.TestRunner, UnityEngine.TestRunner | COCOFLOW_UNITASK_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Editor/ProjectScaffold/CoCoFlow.Tests.Editor.ProjectScaffold.asmdef` | UnityEditor.TestRunner, UnityEngine.TestRunner | — | — |
| `Tests/Editor/StateGraph/AuthoringDependencyFixtures/CoCoFlow.Tests.StateGraphAuthoringDependencyFixtures.asmdef` | UnityEditor.TestRunner, UnityEngine.TestRunner | — | — |
| `Tests/Editor/StateGraph/CoCoFlow.Tests.Editor.StateGraph.asmdef` | UnityEditor.TestRunner, UnityEngine.TestRunner | — | — |
| `Tests/Editor/StateGraph/Fixtures/CoCoFlow.Tests.StateGraphAuthoringFixtures.asmdef` | UnityEditor.TestRunner, UnityEngine.TestRunner | — | — |
| `Tests/Editor/StateGraph/LegacyCoreDependencyFixtures/CoCoFlow.Tests.StateGraphLegacyCoreDependencyFixtures.asmdef` | UnityEditor.TestRunner, UnityEngine.TestRunner | — | — |
| `Tests/Editor/StateGraph/PlayerMetadataFixtures/Sections/CoCoFlow.Tests.StateGraphPlayerMetadataSections.asmdef` | UnityEditor.TestRunner, UnityEngine.TestRunner | — | — |
| `Tests/Editor/StateGraph/PlayerMetadataFixtures/Values/CoCoFlow.Tests.StateGraphPlayerMetadataValues.asmdef` | UnityEditor.TestRunner, UnityEngine.TestRunner | — | — |
| `Tests/Editor/StateGraph/TransitiveDependencyFixtures/Author/CoCoFlow.Tests.StateGraphTransitiveDependencyAuthor.asmdef` | UnityEditor.TestRunner, UnityEngine.TestRunner | — | — |
| `Tests/Editor/StateGraph/TransitiveDependencyFixtures/Helper/CoCoFlow.Tests.StateGraphTransitiveDependencyHelper.asmdef` | UnityEditor.TestRunner, UnityEngine.TestRunner | — | — |
| `Tests/Editor/StateGraphHost/CoCoFlow.Tests.Editor.StateGraphHost.asmdef` | UnityEditor.TestRunner, UnityEngine.TestRunner | — | — |
| `Tests/Fixtures/MapExternalTa/Runtime/CoCoFlow.Fixtures.ExternalMapTa.asmdef` | UniTask, UnityEngine.TestRunner | COCOFLOW_UNITASK_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Runtime/Animation/CoCoFlow.Tests.Runtime.Animation.ReplaySpike.asmdef` | UnityEngine.TestRunner | — | — |
| `Tests/Runtime/Animation/Contracts/CoCoFlow.Tests.Runtime.Animation.asmdef` | UnityEngine.TestRunner | — | — |
| `Tests/Runtime/Animation/DOTween/CoCoFlow.Tests.Runtime.Animation.DOTween.asmdef` | UnityEngine.TestRunner | COCOFLOW_DOTWEEN_SUPPORT | — |
| `Tests/Runtime/CoCoFlow.Tests.Runtime.ContextLifecycle.asmdef` | Unity.Cinemachine, Unity.InputSystem, Unity.InputSystem.TestFramework, UnityEngine.TestRunner | — | — |
| `Tests/Runtime/Content/Addressables/CoCoFlow.Tests.Runtime.Content.Addressables.asmdef` | UniTask, Unity.Addressables, Unity.ResourceManager, UnityEngine.TestRunner | COCOFLOW_UNITASK_SUPPORT, COCOFLOW_ADDRESSABLES_2_9_OR_NEWER | com.unity.addressables [2.9.1,3.0.0)→COCOFLOW_ADDRESSABLES_2_9_OR_NEWER; com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Runtime/Content/CoCoFlow.Tests.Runtime.ContentConsumers.asmdef` | UniTask, UnityEngine.TestRunner | COCOFLOW_UNITASK_SUPPORT, COCOFLOW_DOTWEEN_SUPPORT, UNITASK_DOTWEEN_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Runtime/Content/DirectScene/CoCoFlow.Tests.Runtime.Content.DirectScene.asmdef` | UniTask, UnityEngine.TestRunner | COCOFLOW_UNITASK_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Runtime/Input/CoCoFlow.Tests.Runtime.Input.asmdef` | Unity.InputSystem, Unity.InputSystem.TestFramework, UnityEngine.TestRunner | — | — |
| `Tests/Runtime/Localization/CoCoFlow.Tests.Runtime.Localization.asmdef` | Unity.Localization, Unity.ResourceManager, Unity.TextMeshPro, UnityEngine.TestRunner | COCOFLOW_UNITASK_SUPPORT, COCOFLOW_DOTWEEN_SUPPORT, UNITASK_DOTWEEN_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Runtime/Map/Addressables/CoCoFlow.Tests.Runtime.Map.Addressables.asmdef` | UniTask, Unity.Addressables, Unity.ResourceManager, UnityEngine.TestRunner | COCOFLOW_UNITASK_SUPPORT, COCOFLOW_ADDRESSABLES_2_9_OR_NEWER | com.unity.addressables [2.9.1,3.0.0)→COCOFLOW_ADDRESSABLES_2_9_OR_NEWER; com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Runtime/Map/CoCoFlow.Tests.Runtime.Map.asmdef` | UniTask, UnityEngine.TestRunner | COCOFLOW_UNITASK_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Runtime/Map/DirectScene/CoCoFlow.Tests.Runtime.Map.DirectScene.asmdef` | UniTask, UnityEngine.TestRunner | COCOFLOW_UNITASK_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Runtime/Map/Pooling/CoCoFlow.Tests.Runtime.Map.Pooling.asmdef` | UniTask, UnityEngine.TestRunner | COCOFLOW_UNITASK_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Runtime/Map/PublicSdk/CoCoFlow.Tests.Runtime.Map.PublicSdk.asmdef` | UniTask, UnityEngine.TestRunner | COCOFLOW_UNITASK_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Runtime/Map/Temporal/CoCoFlow.Tests.Runtime.Map.Temporal.asmdef` | UniTask, UnityEngine.TestRunner | COCOFLOW_UNITASK_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Runtime/Pooling/CoCoFlow.Tests.Runtime.Pooling.asmdef` | UniTask, UnityEngine.TestRunner | COCOFLOW_UNITASK_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Runtime/Pooling/Temporal/CoCoFlow.Tests.Runtime.Pooling.Temporal.asmdef` | UniTask, UnityEngine.TestRunner | COCOFLOW_UNITASK_SUPPORT | com.cysharp.unitask [2.5.11,3.0.0)→COCOFLOW_UNITASK_SUPPORT |
| `Tests/Runtime/StateGraphHost/CoCoFlow.Tests.Runtime.StateGraphHost.asmdef` | UnityEngine.TestRunner | — | — |
| `Tests/Runtime/StateGraphHost/Fixtures/CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures.asmdef` | UnityEngine.TestRunner | — | — |
