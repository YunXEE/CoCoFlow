# CoCoFlow Rigging Samples

Rigging Samples demonstrate the 0.3.9 Animation module foot IK and foot lock operation pattern. They are intentionally small: State Layer scripts call `AnimRigCharacterController`, while the rigging components write foot target transforms for Unity Animation Rigging constraints.

## Requirements

- Install Unity Animation Rigging in the project.
- Open `CoCoFlow/Setup/Setup Assistant` and refresh status if you want to confirm the dependency is detected.

## Suggested Player Prefab

```text
PlayerRoot
  CharacterContextProvider
  CharacterLocomotion
  AnimRigCharacterController
  AnimRigFootDriver
  LeftFootTarget
  RightFootTarget
  Visual
    Animator
    RigBuilder
  CoCoStates
    CoCoStateController
    CCS_RiggingPlayer_Idle
    CCS_RiggingPlayer_Move
    CCS_RiggingPlayer_Jump
```

Wire `AnimRigFootDriver` left/right bindings to the animated foot bones and IK target transforms. The target transforms should be consumed by the project's Animation Rigging constraints. `AnimRigCharacterController` resolves an Animator on the current object or in children, so a `PlayerRoot/Visual/model Animator` layout is supported.

## State Pattern

- Foot IK is disabled by default; states that need it call `SetFootRigEnabled(true)`.
- Idle and Move call `SetFootLockMode(AnimRigFootLockMode.AnimationDriven)` and expect Animator Controller float parameters driven by animation curves named `CoCoFlow_LeftFootPlant` and `CoCoFlow_RightFootPlant`.
- Jump calls `ReleaseAllFeet()` and `SetFootRigEnabled(false)`.
- Authored state scripts or project-side SMB bridges can call `RiggingSampleFootEventBridge` to explicitly plant or release feet.
- `AnimRigFootLockMode.Automatic` remains a velocity fallback for temporary characters without curves.

This sample does not provide a full character animation graph or procedural locomotion system.
