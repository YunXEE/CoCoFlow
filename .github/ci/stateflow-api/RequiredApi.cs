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
    internal sealed class ExternalInputSource : MonoBehaviour, ICoCoIntentFrameSource<InputIntentData>
    {
        bool ICoCoIntentFrameSource<InputIntentData>.TrySample(in CoCoTickFrame tick, out InputIntentData intent)
        {
            intent = new InputIntentData(default, ReadOnlyMemory<RawInputRecord>.Empty);
            return true;
        }
    }

    internal sealed class ExternalManagedIntent : ICoCoIntentFrameSource<string>
    {
        bool ICoCoIntentFrameSource<string>.TrySample(in CoCoTickFrame tick, out string intent)
        {
            intent = string.Empty;
            return true;
        }
    }

    internal sealed class ExternalOperator : ICoCoOperator
    {
        [CoCoOperatorDefinition]
        private static CoCoStandardOperatorDefinition Define => Definition(default, default, default, default, default);

        CoCoOperatorDescriptor ICoCoOperator.Descriptor => Define.Descriptor;

        private static CoCoStandardOperatorDefinition Definition(CoCoOperatorId id,
            CoCoStateBlockId block, CoCoStateSlotId slot, CoCoEventTypeId type, CoCoEventDomainId domain)
        {
            var builder = new CoCoStandardOperatorBuilder();
            CoCoOperationSectionRequirement operation = builder.Require<IExternalSection>();
            builder.OwnOutcome(block, slot, CoCoContextProjection.Temporal, CoCoContextRestorePolicy.Stored, 0, 1);
            CoCoEventOutboxRequirement output = builder.Emit<int>(type, domain, 98);
            return builder.Freeze<ExternalOperator>(id);
        }

        bool ICoCoOperator.TryExecute(in CoCoOperatorExecutionContext context, out CoCoOperatorOutcome outcome)
        {
            bool found = context.TryGet<IExternalSection>(default, out var entry);
            int value = 1;
            bool bySlot = context.TryWriteOutcome(default(CoCoStateSlot<int>), in value);
            bool byId = context.TryWriteOutcome(default(CoCoStateSlotId), in value);
            outcome = default;
            return found && bySlot && byId;
        }
    }

    internal static class RequiredApi
    {
        private delegate bool HostStep(double deltaTime, out CoCoDiagnostic diagnostic);

        internal static void HostLifecycle(CoCoStateGraphHost host)
        {
            HostStep step = host.TryStep;
            bool started = host.TryStart(out _);
            bool suspended = host.TrySuspend(out _);
            bool resumed = host.TryResume(out _);
            bool stopped = host.TryStop(out _);
            bool disposed = host.TryDispose(out _);

            CoCoRuntimeLifecycleState lifecycle = host.Lifecycle;
            CoCoRuntimeFault fault = host.Fault;
            CoCoDiagnostic diagnostic = host.LastDiagnostic;
            CoCoGraphInstanceId instance = host.GraphInstanceId;
            IReadOnlyList<CoCoActivePath> paths = host.ActivePaths;
            CoCoContextFrame frame = host.CurrentContext;
            ICoCoStateFlowTrace trace = host.Trace;
            CoCoStateGraphAsset asset = host.StateGraphAsset;
            ulong inputRevision = host.InputAuthorityRevision;
            CoCoTickFrame tick = frame.Header.TickFrame;
            CoCoContextRevision revision = frame.Revision;
            if (frame.Retain())
            {
                if (frame.Layout.TryResolveSlot<int>(default, out var slot))
                    _ = frame.Read(slot);
                bool released = frame.Release();
            }
        }

        internal static void HostTemporalAndEvents(CoCoStateGraphHost host, in CoCoEventPacket<int> packet)
        {
            bool began = host.TryBeginTemporalPreview(out _);
            bool previewed = host.TryPreviewTemporal(0, out _);
            bool confirmed = host.TryConfirmTemporalRestore(out _);
            bool cancelled = host.TryCancelTemporalPreview(out _);
            bool corrected = host.TryCorrectWorld(out _);
            CoCoTemporalState state = host.TemporalState;
            int capacity = host.TemporalHistoryCapacity;
            bool requiresCorrection = host.RequiresWorldCorrection;
            bool valid = host.TryValidateRestore(host.CurrentContext, default, out CoCoContextCommitStatus _);
            CoCoInboxEnqueueResult result = host.TryEnqueueLocal(in packet);
        }

        internal static void WriteOperatorEvent(CoCoOperatorExecutionContext context,
            CoCoEventTypeId type, CoCoEventDomainId domain, CoCoEventOutboxTarget target, int payload)
        {
            if (CoCoEventOutboxRequirement.TryCreate<int>(type, domain, 1, out var requirement, out _))
            {
                CoCoEventOutboxWriteResult written = context.EventOutbox.TryWrite(requirement, target, in payload);
            }
        }

        internal static void CompileGraph(CoCoStateGraphAsset asset, CoCoGraphDescriptorCatalog catalog)
        {
            CoCoStateGraphAssetCompileResult result = new CoCoStateGraphAssetCompiler().Compile(asset, catalog);
            bool succeeded = result.Succeeded;
            bool hasErrors = result.HasErrors;
            CoCoCompiledStateGraph graph = result.Graph;
            IReadOnlyList<CoCoGraphDiagnostic> diagnostics = result.Diagnostics;
        }

        internal static void ReadIntent<TIntent>(ICoCoIntentFrame frame, CoCoIntentHandle<TIntent> handle)
        {
            bool present = frame.IsPresent(handle);
            bool byHandle = frame.TryGet(handle, out TIntent value);
            bool byType = frame.TryFirst<TIntent>(out value);
        }

        internal static void Provider(IReadOnlyList<Assembly> assemblies)
        {
            CoCoStandardBindingProvider standard = CoCoStandardBindingProvider.Build(assemblies);
            CoCoStandardBindingProvider discovered = CoCoStandardBindingProvider.BuildForAssemblyOf<ExternalState>();
            CoCoGraphDescriptorCatalog catalog = standard.Catalog;
            bool installed = CoCoStateGraphProjectBindings.TryInstall(standard, out _);
        }
    }
}
