using System;

// Host 将已订阅的事件交给项目组件筛选，只有通过筛选的载荷才进入实例的 State 收件箱。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>由项目组件筛选指定类型的入站事件，决定是否交给本实例的 State。</summary>
    public interface IEventInboxAdapter<TEvent> where TEvent : unmanaged
    {
        /// <summary>在事件进入 State 的直接收件通道前检查载荷，返回 true 才允许该事件进入本步可见的队列。</summary>
        bool Accept(in TEvent payload);
    }
}
