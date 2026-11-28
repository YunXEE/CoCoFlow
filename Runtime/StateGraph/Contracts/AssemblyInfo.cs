using System.Runtime.CompilerServices;

// 允许图绑定、编辑器与包测试使用纯合同程序集的框架入口，普通玩法程序集通过公开合同接入。
[assembly: InternalsVisibleTo("CoCoFlow.Editor.StateGraph")]
[assembly: InternalsVisibleTo("CoCoFlow.Runtime.StateGraph")]
[assembly: InternalsVisibleTo("CoCoFlow.Tests.Editor.StateGraph")]
[assembly: InternalsVisibleTo("CoCoFlow.Tests.Runtime.StateGraphHost")]
[assembly: InternalsVisibleTo("CoCoFlow.Runtime.Modules.Input")]
[assembly: InternalsVisibleTo("CoCoFlow.Tests.StateGraphAuthoringFixtures")]
[assembly: InternalsVisibleTo("CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures")]
[assembly: InternalsVisibleTo("CoCoFlow.Runtime.Locomotion.Contracts")]
