using CoCoFlow.Runtime.Input.Contracts;

using System;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using UnityEngine;

// 此样例模拟引用 Unity 程序集的项目组件，编译 Host 生命周期、Operator 和事件接口的真实调用。
namespace CoCoFlow.CI
{
    /// <summary>模拟场景组件向 Host 提供一份玩家输入快照。</summary>
    internal sealed class ExternalInputSource : MonoBehaviour, IIntentFrameSource<InputIntentContract>
    {
        bool IIntentFrameSource<InputIntentContract>.TrySample(in TickFrame tick, out InputIntentContract intent)
        {
            intent = new InputIntentContract(default, ReadOnlyMemory<RawInputRecord>.Empty);
            return true;
        }
    }

    /// <summary>模拟以托管字符串作为 Intent 载荷的自定义输入来源。</summary>
    internal sealed class ExternalManagedIntent : IIntentFrameSource<string>
    {
        bool IIntentFrameSource<string>.TrySample(in TickFrame tick, out string intent)
        {
            intent = string.Empty;
            return true;
        }
    }

    /// <summary>验证项目实现可用已声明的输入、结果槽和出件目标执行图的一步。</summary>
    internal sealed class ExternalOperator : IOperator
    {
        [OperatorDefinition]
        private static HostOperatorDefinition Define => Definition(default, default, default, default, default);

        OperatorDescriptor IOperator.Descriptor => Define.Descriptor;

        private static HostOperatorDefinition Definition(OperatorId id,
            StateBlockId block, StateSlotId slot, EventTypeId type, EventDomainId domain)
        {
            var builder = new HostOperatorBuilder();
            OperationRequirement operation = builder.Require<IExternalOperation>();
            builder.OwnOutcome(block, slot, ContextProjection.Temporal, ContextRestorePolicy.Stored, 0, 1);
            EventOutboxRequirement output = builder.Emit<int>(type, domain, 98);
            return builder.Freeze<ExternalOperator>(id);
        }

        bool IOperator.TryExecute(in OperatorExecutionContext context, out OperatorOutcome outcome)
        {
            bool found = context.TryGet<IExternalOperation>(default, out var entry);
            if (context.PreviousContext.Layout.TryResolveSlot<int>(default, out var slot))
                _ = context.PreviousContext.Read(slot);
            int value = 1;
            bool bySlot = context.TryWriteOutcome(default(StateSlot<int>), in value);
            bool byId = context.TryWriteOutcome(default(StateSlotId), in value);
            outcome = default;
            return found && bySlot && byId;
        }
    }

    /// <summary>项目声明双来源整数输入，分别为每个 Host 创建归并器。</summary>
    internal sealed class ExternalTwoSources : HostIntentRegistration<int, ExternalReducer, ExternalReducerFactory>
    {
        public ExternalTwoSources() : base(CreateId(), 2, 902) { }

        private static IntentId CreateId()
        {
            IntentId.TryCreate(901, 902, out var id);
            return id;
        }
    }

    internal struct ExternalReducer : IIntentReducer<int>
    {
        public int Reduce(in int current, in int candidate) => current + candidate;
    }

    internal sealed class ExternalReducerFactory : IIntentReducerFactory<int, ExternalReducer>
    {
        public ExternalReducer Create(GraphInstanceId graphInstanceId) => default;
    }

    /// <summary>项目可在不同恢复入口同步读取同一个类型化槽。</summary>
    internal sealed class ExternalRestore : MonoBehaviour, IHostRestoreBinding, IHostPersistenceRestoreOperator, IHostTemporalRestoreOperator
    {
        public OperatorDescriptor Descriptor { get; private set; }
        public bool TryExecute(in OperatorExecutionContext context, out OperatorOutcome outcome)
        {
            outcome = default;
            return true;
        }

        public bool TryRestore(in HostRestoreContext context, out CoCoDiagnostic diagnostic) =>
            TryApply(context, out diagnostic);

        public bool TryApply(in HostRestoreContext context, out CoCoDiagnostic diagnostic)
        {
            HostRestoreKind operation = context.ApplyKind;
            HostRestoreReader reader = context.Reader;
            diagnostic = CoCoDiagnostic.None;
            return reader.TryRead(default(StateSlotId), out int value);
        }
    }

    /// <summary>从包外编译 Host、Provider、Operator 和项目挂载组件的实际调用。</summary>
    internal static class RequiredApi
    {
        /// <summary>为编译样例保留手动 Host 步进的参数及返回合同。</summary>
        private delegate bool HostStep(double deltaTime, out CoCoDiagnostic diagnostic);

        internal static void HostLifecycle(StateGraphHost host)
        {
            HostStep step = host.TryStep;
            bool started = host.TryStart(out _);
            bool suspended = host.TrySuspend(out _);
            bool resumed = host.TryResume(out _);
            bool stopped = host.TryStop(out _);
            bool disposed = host.TryDispose(out _);

            RuntimeLifecycleState lifecycle = host.Lifecycle;
            RuntimeFault fault = host.Fault;
            CoCoDiagnostic diagnostic = host.LastDiagnostic;
            GraphInstanceId instance = host.GraphInstanceId;
            IReadOnlyList<ActivePath> paths = host.ActivePaths;
            ContextFrame frame = host.CurrentContext;
            IStateFlowTrace trace = host.Trace;
            StateGraphAsset asset = host.StateGraphAsset;
            ulong inputRevision = host.InputAuthorityRevision;
            TickFrame tick = frame.Header.TickFrame;
            ContextRevision revision = frame.Revision;
            if (frame.Retain())
            {
                if (frame.Layout.TryResolveSlot<int>(default, out var slot))
                    _ = frame.Read(slot);
                bool released = frame.Release();
            }
        }

        internal static void HostTemporalAndEvents(StateGraphHost host, in EventPacket<int> packet)
        {
            bool began = host.TryBeginTemporalPreview(out _);
            bool previewed = host.TryPreviewTemporal(0, out _);
            bool confirmed = host.TryConfirmTemporalRestore(out _);
            bool cancelled = host.TryCancelTemporalPreview(out _);
            bool corrected = host.TryCorrectWorld(out _);
            HostHistoryState state = host.TemporalState;
            HostHistoryMode mode = state.Mode;
            HostHistoryFrame preview = state.Preview;
            int capacity = host.TemporalHistoryCapacity;
            bool requiresCorrection = host.RequiresWorldCorrection;
            bool valid = host.TryValidateRestore(host.CurrentContext, default, out ContextCommitStatus _);
            InboxEnqueueResult result = host.TryEnqueueLocal(in packet);
        }

        internal static void WriteOperatorEvent(OperatorExecutionContext context,
            EventTypeId type, EventDomainId domain, EventOutboxTarget target, int payload)
        {
            if (EventOutboxRequirement.TryCreate<int>(type, domain, 1, out var requirement, out _))
            {
                EventOutboxWriteResult written = context.EventOutbox.TryWrite(requirement, target, in payload);
            }
        }

        internal static void ReadIntent<TIntent>(IIntentFrame frame, IntentHandle<TIntent> handle)
        {
            bool present = frame.IsPresent(handle);
            bool byHandle = frame.TryGet(handle, out TIntent value);
            bool byType = frame.TryFirst<TIntent>(out value);
        }

        internal static void Provider(IReadOnlyList<Assembly> assemblies)
        {
            HostBindingProvider standard = HostBindingProvider.Build(assemblies);
            HostBindingProvider discovered = HostBindingProvider.BuildForAssemblyOf<ExternalState>();
            GraphDescriptorCatalog catalog = standard.Catalog;
            bool installed = HostProjectBindings.TryInstall(standard, out _);
        }
    }
}
