using System.Runtime.CompilerServices;

// 允许图绑定、编辑器与包测试使用本程序集的框架入口，普通玩法程序集通过公开合同接入。
[assembly: InternalsVisibleTo("CoCoFlow.Runtime.Pooling.Temporal")]
[assembly: InternalsVisibleTo("CoCoFlow.Runtime.Modules.Map.Temporal")]
[assembly: InternalsVisibleTo("CoCoFlow.Runtime.Modules.Locomotion")]
[assembly: InternalsVisibleTo("CoCoFlow.Runtime.Modules.Persistence")]
[assembly: InternalsVisibleTo("CoCoFlow.Tests.Runtime.Pooling.Temporal")]
[assembly: InternalsVisibleTo("CoCoFlow.Tests.Runtime.StateGraphHost")]
[assembly: InternalsVisibleTo("CoCoFlow.Editor.StateGraph")]
[assembly: InternalsVisibleTo("CoCoFlow.Tests.Editor.StateGraph")]

// 0.4.10 正常保存的配置使用这一程序集名。CLR 保留相同类型的程序集转发。
[assembly: TypeForwardedTo(typeof(CoCoFlow.Runtime.StateGraph.FieldConfig))]
[assembly: TypeForwardedTo(typeof(CoCoFlow.Runtime.StateGraph.EmptyStateConfig))]
