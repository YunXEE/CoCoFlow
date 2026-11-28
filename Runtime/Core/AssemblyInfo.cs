using System.Runtime.CompilerServices;

// 这里声明 Runtime Core 的友元程序集。
// StateGraphHost 通过这个权限安装 Unity 日志输出端，并接入 Unity 对已销毁监听者的判定。
[assembly: InternalsVisibleTo("CoCoFlow.Runtime.StateGraph")]
