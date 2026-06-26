# Network Context Sync Plan

This document is the implementation plan for the new Network Samples. It intentionally replaces the old NetworkSamples approach.

## Core Idea

CoCoFlow already routes behavior through intent and context:

```text
InputReader / AI / Spline / Network Source
  -> CharacterContextProvider or NetCharacterContextBridge
  -> CoCoInputIntent / CharacterContext facts
  -> CoCoStateController
  -> ordered State Layers
  -> State scripts
  -> Locomotion, Navigation, Animation, Combat
```

The Fusion layer should preserve that shape. It should bridge network data into existing CoCoFlow interfaces instead of creating a parallel player/enemy controller hierarchy.

## Required Bridges

| Script | Interface | Responsibility |
|---|---|---|
| `NetInputReaderBridge` | `ICoCoIntentSource<CoCoInputIntent>` | Convert Fusion input into CoCoFlow input intent. |
| `NetCharacterContextBridge` | `ICoCoContextProvider<CharacterContext>` | Replace or feed the local `CharacterContextProvider` by synchronizing identity, lifecycle, semantic/action state, motion, resources, perception ids, character intent facts, and `CharacterContext.Navigation` facts. |
| `NetworkCameraRigBinder` | `ICameraDirector` bridge | Activate/deactivate a local `CameraRig` for the client that owns, controls, or spectates a subject. |
| `NetEntityReferenceResolver` | optional helper | Resolve stable ids or Fusion object refs into local transforms. |

## Player Flow

```text
Local InputReader
  -> Fusion input
  -> NetInputReaderBridge on StateAuthority
  -> CharacterInputDriver
  -> CharacterContext.Intent
  -> CoCoStateController
```

## Enemy Flow

```text
EnemyBrain and EnemySpline on StateAuthority
  -> CharacterContext
  -> CoCoStateController
  -> State scripts
  -> CharacterNavigationMotor / CharacterLocomotion
```

Proxies should not run `EnemyBrain` or `EnemySpline`; they apply synchronized Context snapshots.

## Boundaries

- No Fusion dependency in package Runtime.
- No dedicated network State controller.
- No direct synchronization of Unity `Transform` references.
- No direct network ownership of Animator, Locomotion, or Combat scripts.
- A character should expose one synchronized `CharacterContext`; navigation facts are part of that payload, not a second State context.
- A character state system should keep one `CoCoStateController` with explicit ordered State Layers. Network adapters should not create parallel controller trees.
- Animation Rigging and IK data should enter through explicit Context facts or operation components only after the business project stabilizes that contract.

## Camera 边界

Camera 故意保持本地化。Network adapter 不应该把 `CameraDirector` 当前 winner 或 `CameraRig` 当前 mode id 同步成权威 gameplay state；本地客户端只需要在自己拥有、控制或观战某个 subject 时，激活对应 `CameraRig` 并按本地规则调整 priority。

`NetworkCameraRigBinder` 是 sample 侧的桥接脚本。Fusion 侧代码可以在 network object spawn、authority 变化或观战目标变化时调用 `SetLocalCameraAuthority(Object.HasInputAuthority)`，或者传入等价的本地观察者判断。
