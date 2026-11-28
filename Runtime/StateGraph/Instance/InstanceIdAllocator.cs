// Host 启动时从这里取得实例编号；子系统重新注册时重新从 1 分配。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>为当前运行会话内新建的图实例分配非零编号。</summary>
    internal static class InstanceIdAllocator
    {
        private static ulong _nextValue = 1UL;

        internal static GraphInstanceId Next()
        {
            ulong value = _nextValue++;
            if (value == 0UL)
            {
                value = _nextValue++;
            }

            GraphInstanceId.TryCreate(value, out GraphInstanceId id);
            return id;
        }

        internal static void Reset() => _nextValue = 1UL;
    }
}
