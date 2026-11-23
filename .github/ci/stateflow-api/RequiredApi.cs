// Compile-only contract. Each method models a supported external call site.
// The consumer is a separate assembly with no InternalsVisibleTo grant.
using System;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.StateGraphHost;
using UnityEngine;

namespace CoCoFlow.CI
{
    internal sealed class ExternalInputSource : MonoBehaviour, IIntentFrameSource<InputIntentData>
    {
        bool IIntentFrameSource<InputIntentData>.TrySample(in TickFrame tick, out InputIntentData intent)
        {
            intent = new InputIntentData(default, ReadOnlyMemory<RawInputRecord>.Empty);
            return true;
        }
    }

    internal sealed class ExternalManagedIntent : IIntentFrameSource<string>
    {
        bool IIntentFrameSource<string>.TrySample(in TickFrame tick, out string intent)
        {
            intent = string.Empty;
            return true;
        }
    }

    internal sealed class ExternalOperator : IOperator
    {
        [OperatorDefinition]
        private static StandardOperatorDefinition Define => Definition(default, default, default, default, default);

        OperatorDescriptor IOperator.Descriptor => Define.Descriptor;

        private static StandardOperatorDefinition Definition(OperatorId id,
            StateBlockId block, StateSlotId slot, EventTypeId type, EventDomainId domain)
        {
            var builder = new StandardOperatorBuilder();
            OperationSectionRequirement operation = builder.Require<IExternalSection>();
            builder.OwnOutcome(block, slot, ContextProjection.Temporal, ContextRestorePolicy.Stored, 0, 1);
            EventOutboxRequirement output = builder.Emit<int>(type, domain, 98);
            return builder.Freeze<ExternalOperator>(id);
        }

        bool IOperator.TryExecute(in OperatorExecutionContext context, out OperatorOutcome outcome)
        {
            bool found = context.TryGet<IExternalSection>(default, out var entry);
            int value = 1;
            bool bySlot = context.TryWriteOutcome(default(StateSlot<int>), in value);
            bool byId = context.TryWriteOutcome(default(StateSlotId), in value);
            outcome = default;
            return found && bySlot && byId;
        }
    }

    internal static class RequiredApi
    {
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
            TemporalState state = host.TemporalState;
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

        internal static void CompileGraph(StateGraphAsset asset, GraphDescriptorCatalog catalog)
        {
            StateGraphAssetCompileResult result = new StateGraphAssetCompiler().Compile(asset, catalog);
            bool succeeded = result.Succeeded;
            bool hasErrors = result.HasErrors;
            CompiledStateGraph graph = result.Graph;
            IReadOnlyList<GraphDiagnostic> diagnostics = result.Diagnostics;
        }

        internal static void ReadIntent<TIntent>(IIntentFrame frame, IntentHandle<TIntent> handle)
        {
            bool present = frame.IsPresent(handle);
            bool byHandle = frame.TryGet(handle, out TIntent value);
            bool byType = frame.TryFirst<TIntent>(out value);
        }

        internal static void Provider(IReadOnlyList<Assembly> assemblies)
        {
            StandardBindingProvider standard = StandardBindingProvider.Build(assemblies);
            StandardBindingProvider discovered = StandardBindingProvider.BuildForAssemblyOf<ExternalState>();
            GraphDescriptorCatalog catalog = standard.Catalog;
            bool installed = StateGraphProjectBindings.TryInstall(standard, out _);
        }
    }
}
