# CoCoFlow 仓库规则

## 身份与权威

- 本仓库是 `com.yunxee.cocoflow` UPM 包体真相：契约以 Runtime/Editor 源码与 asmdef 为准。
- 宿主工程只提供集成证据，不定义包体契约。
- 外部知识库（任务/缺陷/审计系统）的坐标与用法见维护者全局配置，不在本文件。

## 交付流程

- 交付任务走四阶段工作流：方案 → 实施 → 分诊 → 验收（delivery-workflow skill）。
- 方案文书是实施的唯一依据；每个阶段以文书交接，不依赖会话记忆。
- 缺陷一律登记台账（BUG-NNN 编号）后修复，修复分支命名
  fix/<版本>-P<三位任务序号>-bug<编号>-<主题>。
- 新任务编号格式：<项目>-<版本>-<序号>。

## 工具链

- Unity 自动化验证走 batchmode / Unity CLI；编辑器版本以宿主工程关联为准。
- 深度静态分析：jb inspectcode <宿主>.sln -o=<sarif 输出>
  （对比基线；不默认执行 cleanupcode）。
- 规模红线：单任务新增 ≤4500 行预警 / ≤6000 行红线（可经方案显式声明豁免）。
- Player 构建、场景与视觉验收归维护者手工执行。

## 交付工作流配置（项目级；库坐标等私有配置见维护者全局层，不进仓库）

- 分支格式：`<类型>/<版本>-P<三位任务序号>-<具体内容>`；类型使用 `feat` / `fix` / `refactor`，例如 `feat/0.4.5-P001-config-collections-decimal-input`；Bug 修复在具体内容中保留 `bug<编号>`
- PR 名称：`【X.X.X】<类型>：<具体修改内容>`，例如 `【0.4.5】feat：支持 Config 集合与小数输入`
- rc 阶段分支：`rc/<stage>-<topic>`（stage 0-4 对应 v0.4.0-rc.N tag；final = flip 正式版 v0.4.0）
- 合并目标：`dev/X.X.X`，`X.X.X` 为任务所属版本号（例如 0.4.5 对应 `dev/0.4.5`），从不直接 master
- 规模红线：单任务新增 ≤4500 行预警 / ≤6000 行红线（方案可显式豁免）
- 审计渠道示例：机器人 @codex review（PR 评论触发）+ CodeRabbit（自动）
- Unity 验证：batchmode / Unity CLI，结果 XML 归档 .ci-artifacts/<SHA>/<宿主>/<版本>/
- 静态分析基线：jb inspectcode，输出 .artifacts/resharper.sarif 对比

## Z/Y 发布策略（003 新门禁启用后生效）

- 启用前提：固定环境完整 Unity CI 已取得真实红/绿证据，维护者已授权
  master/dev required check 从 `CI Static / gate` 切到 `CI / gate`。
- Z（X.Y.Z 增加 Z）保留代码任务本人验收和独立 PR；版本层只做一个收口提交，
  更新版本、短 Changelog、当前安装引用及既有例外版本字段，不另建 Release
  四阶段任务或 release 分支。本节覆盖通用分支规则中的 Z 级 release 层。
- 任务分支仍经独立 PR → `dev/<版本>` → master。收口可在最后任务 PR 内；
  已合完任务时使用单提交小 PR，不绕过 dev/master 保护。
- Y（X.Y.0 增加 Y）保留独立 Release 任务、声明的完整组合/迁移/外部验证和
  维护者版本验收签名。契约、迁移或支持边界变化由维护者裁决验收强度。
- `CI / gate` 对实际必需检查负责；缺 XML、零测试、目标测试程序集未发现、
  失败/取消/必需 Unity 未跑都不能通过。Linux Editor 不证明 Player/其他平台。
- 受测 SHA 和候选 SHA 分列；输入变化就重验受影响部分，不声称新 SHA 实跑过。
  最终集成候选跑完整 CI，Tag 仅核对身份与该提交的 gate，不重跑整套。
- CI 通过不自动授权提交、推送、PR、合并、Tag、Release 或删分支。
- 细则见 `.github/ci/README.md`；新门禁启用前沿用现有有效发布规则。

## 命名与边界

- 遵循 CodeRule.md：层级左倾命名（模块前缀+子类别+名称+后缀），文件夹全称、脚本缩写。
- Core 层 CoCo 前缀；功能模块用自身前缀（Input/Cam/Anim/Map/UI…）。
- Section 拒绝继承，强制组合；公共 API 变更走 Decision Ledger（D# 编号）。
- 0.4 冻结契约全集见 .github/contracts-checklist.md（按需引用）。
