// 允许 Host 编译访问 Core 的内部安装入口。

using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("CoCoFlow.Runtime.StateGraphHost")]
