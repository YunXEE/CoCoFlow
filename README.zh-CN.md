![CoCoFlow](.github/assets/cocoflow-banner.svg)

> **版本**：0.4.6 · **Unity**：6000+
>
> [English](README.md)

CoCoFlow 是面向 Unity 6 的状态流框架，核心包括类型化输入、分层
StateGraph、事务式 Context 提交、Temporal 恢复和显式 Runtime ownership。

## 0.4.6 新内容

- 每个 Layer 可绑定一个 `CoCoAnyStateLogic`，在普通活跃枝之后执行，
  请求的转移满足条件时优先。保留完整 Config/Memory/Intent/Operation 和邮箱
  能力，沿用事务与恢复规则。AnyState Memory 跨换叶保留；重入当前叶只重置
  该叶的 Activation、Memory 和时钟，祖先与 AnyState 保持，作者可每 Tick 请求。
  见 [Runtime 契约](Docs/StateGraphRuntime.md#anystate-one-persistent-logic-per-layer)
  和 [Editor 接线](Docs/StateGraphEditor.md#configuring-anystate)。
- Composite 卡的“整理子级”下提供“收起子机”。跨边界转移共享连线，每个真实
  转移对应一个三角，通过菜单选择原转移。折叠仅存于 Editor Session，保留图数据
  和 Runtime 输入。收起后保留紫色高亮；三角放大、AnyState 常驻玫红边框，
  谱系线透明度降低。见 [子机折叠](Docs/StateGraphEditor.md#collapsing-a-sub-state-machine)。
- PersistenceContext ID 改为在 Prefab 资产上显式维护，变体使用独立覆盖。
  Lock ID、只读 Scan 和 Regenerate ID 支持此流程；生命周期回调不再自动生成
  或去重 ID。既有 ID 保留；重新生成会改变存档身份，多个独立存档实体须使用
  不同身份。见 [身份边界](Docs/Module-Persistence.md#stable-entity-identity)。

没有 AnyState 的旧图无需迁移或补边。Player、IL2CPP/AOT、裁剪、完整 Umbra
动画和脚 IK 仍未验证。修复与兼容边界见 [Changelog](CHANGELOG.md)。

## “成熟”的定义

本次发布中的 **成熟** 表示：公共 Runtime API 稳定、模块已在实际项目中完成过其职责，
并且已知边界能够被准确说明；各版本更新记录中明确列出的变更优先于兼容预期。
它**不表示**架构最新、效能最高、Editor 工具完整、
零缺陷或通过商店级认证。

| 模块 | 状态 | 准确说明 |
|---|---|---|
| Core Engine | **成熟** | Contracts、StateFlow、StateGraph、StateGraphAuthoring Runtime、StateGraphHost；0.4 原生核心。 |
| Camera | **成熟** | 起源于 0.3.9，当前 Rig、priority、mode API 可稳定使用。 |
| Persistence | **成熟** | 起源于 0.3.9，现已支持 schema v2、Container 与 StateGraph ContextFrame 持久化。 |
| UI | **成熟** | 起源于 0.3.9，Panel、Widget、Input、Content ownership API 可用且稳定，但不是高效能 UI 框架。 |
| Map | **不成熟** | 当前实现可用，但公共 API、配置和序列化结构暂不保证兼容。 |
| Pooling | **不成熟** | 当前实现可用，但公共 API、配置和序列化结构暂不保证兼容。 |
| 其他模块 | **暂不评级** | 本次不作成熟或不成熟判断。 |

Core Engine 的成熟声明不包含 StateGraph Editor，也不包含
`Runtime/Core/*.cs` 下旧 EventBus、Services 和 Context 设施。

## Core Flow

```text
Host-local Intent sources       External struct events
          ↓                               ↓
      IntentFrame                  Adapter → Inbox batch
          └──────────────┬────────────────┘
               Layered StateGraph Step
                        ↓
              Finalized OperationFrame
                        ↓
      Operators + staged Context candidate
                        ↓
                    原子提交
                        ├─ committed ContextFrame
                        ├─ Event Outbox + Trace
                        └─ Temporal / persistence projection
```

每个 Actor 独占自己的 Runtime 状态。StateLogic 读取不可变的 Tick 输入，更新激活
Memory，写入已声明的 Operation Section，并暂存待发送事件。Host 在启动前校验 binding，每次只暂存一个候选 Tick；整个
Actor 事务要么一起提交，要么继续保留上一份权威状态。跨对象副作用只从已提交的 Event
Outbox 离开，不通过直接 State callback 传播。

Temporal Restore 是同会话、精确 Layout 的恢复。持久化存档使用独立的 Persistence
Schema；两者不是同一种 Wire Format。

## 安装

在 Unity Package Manager 中使用 Git URL：

```text
https://github.com/YunXEE/CoCoFlow.git#v0.4.6
```

或在 `Packages/manifest.json` 中加入：

```json
{
  "dependencies": {
    "com.yunxee.cocoflow": "https://github.com/YunXEE/CoCoFlow.git#v0.4.6"
  }
}
```

通过 **CoCoFlow > Utility Panel**（CoCoFlow Utility 窗口）检查可选集成依赖和项目设置。
部分集成程序集只有在外部包和对应 support define 存在时才会编译，详见
[依赖矩阵](Docs/DependencyMatrix.md)。

## 文档

- [文档入口](Documentation~/index.md)
- [StateGraph Asset 与 Compiler](Docs/StateGraphCompiler.md)
- [StateGraph Runtime 与 Host](Docs/StateGraphRuntime.md)
- [State Flow / Event 边界](Docs/ContextNetworkBoundary.md)
- [Temporal Rewind](Docs/TemporalRewind.md)
- [Input](Docs/Module-Input.md)
- [Camera](Docs/Module-Camera.md)
- [Persistence](Docs/Module-Persistence.md)
- [UI](Docs/Module-UI.md)
- [Map](Docs/Module-Map.md)
- [Pooling](Docs/ObjectPooling.md)
- [Changelog](CHANGELOG.md)

## 发布策略

CoCoFlow 0.4.x 采用小步迭代。成熟的 Runtime surface 按稳定 API 对待，破坏性变更会在
对应版本中明确列出。Map 与 Pooling 可能在 0.4.x 调整 API、配置和序列化结构。
未评级模块应依据实际实现和模块文档判断，
不能从本表推导成熟度。

## License

MIT
