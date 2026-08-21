<!-- 本模板用中文书写；专有名词（PR/commit/API 名等）用英文 -->

## 任务

- 任务编号：
- 方案版本：v<N>　交付报告：v<N>（内部文书不贴链接，仅记版本号）
- Bug 单（如有）：BUG-NNN（仅编号，不贴台账链接）

## Summary

<改了什么、为什么——三句话内>

## Frozen scope

- Base：`<SHA>`　Final Head：`<SHA>`　Commit range：`<base>..<head>`（N commits）
- Diff：X 文件，+A/-B
- 明确不改的：<契约/模块/依赖>

## 契约变更

<本 PR 冻结/保留/改变的建筑契约；无则写"无"。
改变已冻结契约时：引用 D# 决策与受影响的下游任务。>

## 冻结契约核对

- [ ] 已对照 contracts-checklist.md 中本任务涉及的模块节逐条核对

## 验证（Final Head）

- [ ] 任务验收清单 [AI] 项全绿（数字与证据见交付报告）
- [ ] 测试：EditMode X/X Passed，PlayMode Y/Y Passed（注明宿主/版本）
- [ ] 静态分析：jb inspectcode 无 Error/Warning 级新增
- [ ] Head 变更后证据已重跑（旧提交的绿不算 Final Head 证据）

## 遗留与债务

<登记台账的漂移/延期项；无则写"无">
