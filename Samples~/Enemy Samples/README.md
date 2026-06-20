# CoCoFlow Enemy Samples

Enemy Samples 提供一套最小可运行的 Enemy 示例，用来验证包体 Enemy 基础层如何接入 `CharacterContext`、`CharacterNavigation` 和 `CoCoStateMachineController`。

## Contents

- `Prefabs/P_Enemy_00.prefab`: 已接入 `EnemyBrain`、`EnemySpline`、`CharacterNavigationMotor` 和示例状态机的 Enemy prefab。
- `Assets/EnemyIntent_P_Enemy_00.asset`: `EnemyBrain` 必填意图 SO。
- `Assets/EnemyConfig_P_Enemy_00.asset`: Enemy 基础参数 SO。
- `Scripts/Runtime/States`: 示例状态脚本，属于 sample/add-on，不属于包体 Enemy Runtime。

## Setup Assistant

打开 `CoCoFlow/Setup/Setup Assistant`，在 `Add-ons` 区域勾选 `Enemy Samples` 后点击 `Install Selected Add-ons`。默认安装目标是 `Assets/CoCoFlow/Enemy`。

## Notes

- 这个 sample 不提供伤害、武器或完整 combat runtime。
- 示例状态只演示如何读取 `CharacterContext.Intent` 并写入 `CharacterNavigation`。
- `P_Enemy_00` 需要可用 NavMesh 才能完整验证巡逻和追击移动。
