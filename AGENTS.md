# CoCoFlow 仓库规则

## 身份与权威

- 本仓库是 `com.yunxee.cocoflow` UPM 包体真相：契约以 Runtime/Editor 源码与 asmdef 为准。
- 宿主工程只提供集成证据，不定义包体契约。
- 外部知识库（任务/缺陷/审计系统）的坐标与用法见维护者全局配置，不在本文件。

## 交付流程

- 交付任务走四阶段工作流：方案 → 实施 → 分诊 → 验收（delivery-workflow skill）。
- 方案文书是实施的唯一依据；每个阶段以文书交接，不依赖会话记忆。
- 来源任务已有交付 PR 后，缺陷登记台账（BUG-NNN 编号）再修复；修复分支命名
  fix/<版本>-<来源任务>-bug<编号>-<主题>。
- PR 前的实现未完成、测试失败或验收不通过，继续原任务，不建 Bug 单或修复分支。
- 新任务编号格式：<项目>-<版本>-<序号>。

## 工具链

- Unity 自动化验证走 batchmode / Unity CLI；编辑器版本以宿主工程关联为准。
- 深度静态分析：jb inspectcode <宿主>.sln -o=<sarif 输出>
  （对比基线；不默认执行 cleanupcode）。
- 规模红线：单任务新增 ≤4500 行预警 / ≤6000 行红线（可经方案显式声明豁免）。
- Player 构建、场景与视觉验收归维护者手工执行。

## 交付工作流配置（项目级；库坐标等私有配置见维护者全局层，不进仓库）

- 普通任务分支：`feat/`、`refactor/`、`fix/`，各自通过独立 PR 合入 `dev/<版本>`；
  Bug 修复固定为 `fix/<版本>-<来源任务>-bug<编号>-<主题>`。
- 每个任务使用独立 Paseo workspace 与 worktree；不默认 Wave，不由调度 Agent 本地合流。
- `release/<版本>` 只调整包内容，经 PR 合入版本 dev；版本 dev 最后经发布 PR 合入 master。
- 普通 PR：`[版本号][实际P0X/L0X标识]feat/refactor/fix:实际修改内容`，类型与源分支一致；
  多任务用 `&` 连接。版本级 PR：`[版本号]release:实际发布内容`，不伪造任务标识。
- 创建或送交任何 PR（含 Draft）前，先提供具体可验收成果与步骤，等待维护者本人明确验收。
- 长期保留远端版本 dev 与已有 Tag；release 不是备份。分支清理按维护者授权，本地与远端分别处理。
- 规模红线：单任务新增 ≤4500 行预警 / ≤6000 行红线（方案可显式豁免）
- 审计渠道示例：机器人 @codex review（PR 评论触发）+ CodeRabbit（自动）
- Unity 验证：batchmode / Unity CLI，结果 XML 归档 .ci-artifacts/<SHA>/<宿主>/<版本>/
- 静态分析基线：jb inspectcode，输出 .artifacts/resharper.sarif 对比

## 命名与边界

- 遵循 CodeRule.md：层级左倾命名（模块前缀+子类别+名称+后缀），文件夹全称、脚本缩写。
- Core 层 CoCo 前缀；功能模块用自身前缀（Input/Cam/Anim/Map/UI…）。
- Section 拒绝继承，强制组合；公共 API 变更走 Decision Ledger（D# 编号）。
- 0.4 冻结契约全集见 .github/contracts-checklist.md（按需引用）。
