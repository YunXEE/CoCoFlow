# CoCoFlow Player Samples

Player Samples provides a small third-person style character wiring sample. It shows how one `CharacterContextProvider`, one `CoCoStateController`, and multiple explicit `CoCoStateLayer` entries work together.

## Contents

- `Prefabs/P_Player_00.prefab`: Player prefab wired to `CharacterContextProvider`, `CharacterLocomotion`, `CharacterNavigationMotor`, and `CoCoStateController`.
- `Scripts/Runtime/States`: Sample player states. These are sample/add-on scripts, not package Runtime gameplay code.

## Setup Assistant

Open `CoCoFlow/Setup/Setup Assistant`, select `Player Samples` in the `Add-ons` section, then click `Install Selected Add-ons`. The default install destination is `Assets/CoCoFlow/Player`.

## Notes

- The sample states read `CharacterContext.Intent`, write lightweight navigation facts, and drive `CharacterLocomotion`.
- `P_Player_00` declares explicit `Main`, `FullBody`, and `UpperLayer` State Layers under one `CoCoStateController` so the State Graph Viewer can show the current single-controller layered topology.
- `Main` contains the visible sample states. `FullBody` and `UpperLayer` are placeholder layers that demonstrate parallel layer wiring.
- This sample does not include complete camera, combat, weapon, Animation Rigging, hand IK, or input action setup.
- `P_Player_00` is designed as a state wiring sample and can be extended with your own input driver, network context bridge, or business-side rig driver.
