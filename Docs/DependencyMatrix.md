# CoCoFlow Dependency Matrix

> Documentation baseline: `0.4.0` · Updated 2026-08-29. The source of truth is
> `package.json`, asmdef files, and source. This page summarizes the current
> consumer-facing boundary.

## Package dependencies

| Package | Version | Primary consumer |
|---|---:|---|
| `com.unity.inputsystem` | `1.18.0` | Input, Camera, UI |
| `com.unity.localization` | `1.5.9` | Localization and optional Localization UI |
| `com.unity.cinemachine` | `3.1.6` | Camera |
| `com.unity.ai.navigation` | `2.0.0` | Project/sample integration surface |
| `com.unity.mathematics` | `1.3.3` | Project/sample integration surface |
| `com.unity.nuget.newtonsoft-json` | `3.2.2` | Persistence and Container commands |
| `com.unity.splines` | `2.6.0` | Project/sample integration surface |

Unity Localization resolves Addressables transitively. CoCoFlow's optional
Addressables Content backend compiles only when the resolved Addressables
version is in `[2.9.1,3.0.0)`.

## Core Engine assembly boundary

| Assembly | Direct package/module references | Unity engine reference |
|---|---|---|
| `CoCoFlow.Runtime.Core.Contracts` | none | no |
| `CoCoFlow.Runtime.Core.StateFlow` | Core Contracts | no |
| `CoCoFlow.Runtime.Core.StateGraph` | Core Contracts + StateFlow | no |
| `CoCoFlow.Runtime.Core.StateGraphAuthoring` | Core Contracts + StateFlow + StateGraph | yes |
| `CoCoFlow.Runtime.StateGraphHost` | Core Contracts + StateFlow + StateGraph + StateGraphAuthoring + legacy Core compatibility assembly | yes |

The older `CoCoFlow.Runtime.Core` assembly is a separate Unity-facing
compatibility surface and is not part of the mature Core Engine declaration.

## Optional integrations

UniTask and DOTween are not entries in `package.json`.

| Integration | Compile gate | Current Runtime consumers |
|---|---|---|
| UniTask `[2.5.11,3.0.0)` | `COCOFLOW_UNITASK_SUPPORT` from asmdef `versionDefines` | Content, Map, Pooling, UI |
| Addressables `[2.9.1,3.0.0)` | `COCOFLOW_ADDRESSABLES_2_9_OR_NEWER` plus UniTask support | Content Addressables backend |
| DOTween unitypackage | `COCOFLOW_DOTWEEN_SUPPORT` plus `DOTween.Modules` assembly | UI and optional UI extensions |
| UniTask.DOTween | `UNITASK_DOTWEEN_SUPPORT` plus UniTask/DOTween gates | UI and optional UI extensions |

The Setup Assistant can inspect these packages/assemblies and reconcile the
manual support defines. A stale manual define is not a supported way to bypass
an asmdef version range.

## Runtime module matrix

| Runtime assembly | External assembly references | Compile constraints |
|---|---|---|
| `CoCoFlow.Runtime.Animation.Contracts` | none | none |
| `CoCoFlow.Runtime.Locomotion.Contracts` | none | none |
| `CoCoFlow.Runtime.Modules.Animation` | none | none |
| `CoCoFlow.Runtime.Modules.Locomotion` | none | none |
| `CoCoFlow.Runtime.Modules.Camera` | `Unity.Cinemachine`, `Unity.InputSystem` | none |
| `CoCoFlow.Runtime.Modules.Input` | `Unity.InputSystem` | none |
| `CoCoFlow.Runtime.Modules.Persistence` | none | none |
| `CoCoFlow.Runtime.Modules.Localization` | `Unity.Localization` | none |
| `CoCoFlow.Runtime.Content` | `UniTask` | UniTask support |
| `CoCoFlow.Runtime.Content.Addressables` | `Unity.Addressables`, `Unity.ResourceManager`, `UniTask` | Addressables + UniTask support |
| `CoCoFlow.Runtime.Modules.Map` | `UniTask` | UniTask support |
| `CoCoFlow.Runtime.Modules.Map.Pooling` | `UniTask` | UniTask support |
| `CoCoFlow.Runtime.Modules.Map.Temporal` | `UniTask` | UniTask support |
| `CoCoFlow.Runtime.Pooling` | `UniTask` | UniTask support |
| `CoCoFlow.Runtime.Pooling.Temporal` | none | UniTask support |
| `CoCoFlow.Runtime.Modules.UI` | `DOTween.Modules`, `UniTask`, `UniTask.DOTween`, `Unity.TextMeshPro`, `Unity.InputSystem` | UniTask + DOTween + UniTask.DOTween support |
| `CoCoFlow.Runtime.Modules.Input.UI` | `Unity.InputSystem` | UniTask + DOTween + UniTask.DOTween support |
| `CoCoFlow.Runtime.Modules.Localization.UI` | `Unity.Localization`, `Unity.ResourceManager`, `Unity.TextMeshPro` | UniTask + DOTween + UniTask.DOTween support |

Editor and test assemblies are intentionally omitted from this consumer matrix.
Their exact references remain enforced by asmdef import and package boundary
tests. Test discovery additionally requires `com.yunxee.cocoflow` in the host's
`testables` list and `UNITY_INCLUDE_TESTS`.
