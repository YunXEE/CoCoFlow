using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Core;

// Host 在步进前冻结收件，State 在当前回调读批次。发布的事件由 Host 在该步提交后送出。
namespace CoCoFlow.Runtime.StateGraph
{
    /// <summary>记录生成器在某个 Logic 中发现的收件载荷类型，供 Host 创建收件通道。</summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed class StateInboxEventAttribute : Attribute
    {
        /// <summary>为生成的程序集元数据关联一个 Logic 和其读取的载荷，供绑定时预建直接收件通道。</summary>
        public StateInboxEventAttribute(Type logic, Type payload)
        {
            Logic = logic;
            Payload = payload;
        }

        public Type Logic { get; }
        public Type Payload { get; }
    }

    /// <summary>在有效回调内按载荷类型访问本步冻结的收件批次。</summary>
    public readonly struct StateEventReadView
    {

        #region Public API

        /// <summary>为当前回调取得该载荷的冻结批次，未登记该类型或回调已结束时返回 false。</summary>
        public bool TryGetBatch<TEvent>(out StateEventBatch<TEvent> batch) where TEvent : unmanaged
        {
            batch = default;
            return _context != null && _context.TryGetEventBatch(_generation, out batch);
        }

        #endregion

        #region Internal Logic

        private readonly StateExecutionContext _context;
        private readonly ulong _generation;
        internal StateEventReadView(StateExecutionContext context, ulong generation)
        {
            _context = context;
            _generation = generation;
        }

        #endregion
    }

    /// <summary>为 State 的批次视图按序提供已冻结的事件。</summary>
    internal interface IStateEventBatchReader<TEvent> where TEvent : unmanaged
    {
        int Count { get; }
        TEvent Read(int index);
    }

    /// <summary>让 State 回调按具体载荷定位该步的收件通道。</summary>
    internal interface IStateEventInboxReader
    {
        IStateEventBatchReader<TEvent> Find<TEvent>() where TEvent : unmanaged;
    }

    /// <summary>在产生它的回调内按序读取同类型事件，离开回调后失效。</summary>
    public readonly struct StateEventBatch<TEvent> where TEvent : unmanaged
    {
        private readonly StateExecutionContext _context;
        private readonly ulong _generation;
        private readonly IStateEventBatchReader<TEvent> _lane;
        public bool IsValid => _context != null && _context.IsEventCallbackCurrent(_generation);
        public int Count => IsValid ? _lane.Count : 0;

        #region Public API

        /// <summary>按零基索引读取本回调的冻结载荷。越界或回调过期时输出默认值并返回 false。</summary>
        public bool TryRead(int index, out TEvent payload)
        {
            payload = default;
            if (!IsValid || (uint)index >= (uint)_lane.Count) return false;
            payload = _lane.Read(index);
            return true;
        }

        #endregion

        #region Internal Logic

        internal StateEventBatch(StateExecutionContext context, ulong generation,
            IStateEventBatchReader<TEvent> lane)
        {
            _context = context;
            _generation = generation;
            _lane = lane;
        }

        #endregion
    }

    /// <summary>向当前候选添加 State 发布的事件，整步提交后才向外分发。</summary>
    public readonly struct StateEventOutboxWriter
    {

        #region Public API

        /// <summary>在有效的状态回调中暂存非托管事件，整步提交后发布。没有有效写入者时抛出异常。</summary>
        public void Publish<TEvent>(in TEvent payload) where TEvent : unmanaged
        {
            if (_context == null) throw new InvalidOperationException("State event writer is unavailable.");
            _context.PublishEvent(_generation, in payload);
        }

        #endregion

        #region Internal Logic

        private readonly StateExecutionContext _context;
        private readonly ulong _generation;
        internal StateEventOutboxWriter(StateExecutionContext context, ulong generation)
        {
            _context = context;
            _generation = generation;
        }

        #endregion
    }

    /// <summary>将 State 回调申请发布的载荷转交实例的暂存队列。</summary>
    internal interface IStateEventOutbox
    {
        void Publish<TEvent>(in TEvent payload) where TEvent : unmanaged;
    }
}
