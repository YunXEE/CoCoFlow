# CoCoFlow Player Samples

Player Samples provides a small third-person style character sample wired to `CharacterContext`, `CharacterLocomotion`, `CharacterNavigationMotor`, and `CoCoStateController`.

## Contents

- `Prefabs/P_Player_00.prefab`: Player prefab with the same root/Visual/CoCoStates structure used by the Enemy sample.
- `Scripts/Runtime/States`: Sample CCS player states. These are sample/add-on scripts, not package Runtime gameplay code.

## Setup Assistant

Open `CoCoFlow/Setup/Setup Assistant`, select `Player Samples` in the `Add-ons` section, then click `Install Selected Add-ons`. The default install destination is `Assets/CoCoFlow/Player`.

## Notes

- The sample states read `CharacterContext.Intent` and drive `CharacterLocomotion`.
- `P_Player_00` declares explicit `Main`, `FullBody`, and `UpperLayer` state layers under one `CoCoStateController` so the State Graph Viewer can show single-controller layered topology.
- This sample does not include a complete camera, combat, weapon, IK, or input action setup.
- `P_Player_00` is designed as a state wiring sample and can be extended with your own input driver or network context bridge.
