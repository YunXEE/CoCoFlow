// Compile-only contract. Each method models a supported external call site.
// The consumer is a separate assembly with no InternalsVisibleTo grant.
using System;
using System.Collections.Generic;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using UnityEngine;

namespace CoCoFlow.CI
{
    [CoCoState("ExternalState", ProvidesActionProgress = true)]
    internal sealed class ExternalState : CoCoStateLogic, ICoCoStateEnter, ICoCoStateUpdate, ICoCoStateExit
    {
        [Intent] public InputIntentData Input { get; set; }
        [Operation] public IExternalSection Section { get; set; }
        [Config] public float Duration;
        [Memory] public int Visits;

        void ICoCoStateEnter.OnEnter(CoCoStateExecutionContext context) => ReadInput(Input);

        void ICoCoStateUpdate.Update(CoCoStateExecutionContext context)
        {
            CoCoContextFrameReadView previous = context.PreviousContext;
            if (previous.Layout.TryResolveSlot<int>(default, out var slot))
                _ = previous.Read(slot);
            CoCoOperationSectionField<int> field = context.Operations.ResolveField<IExternalSection, int>(0);
            bool wrote = context.Operations.Write(field, in Visits);
            bool triggered = context.Operations.TryEnableDiscrete<IExternalSection>();
            bool requestedByHandle = context.RequestTransition(default);
            bool requestedByType = context.TryRequestTransitionTo<ExternalState>();
            bool progressed = context.TrySetActionProgress(Duration);
            ExternalMemory memory = context.Memory<ExternalMemory>();
            if (context.Inbox.TryGetBatch<int>(out var batch) && batch.TryRead(0, out int payload))
                context.Outbox.Publish(in payload);
            _ = Section;
        }

        void ICoCoStateExit.OnExit(CoCoStateExecutionContext context) { }

        private static void ReadInput(InputIntentData input)
        {
            CoCoFixedString64 activeMap = input.ActiveMap;
            ReadOnlyMemory<RawInputRecord> changes = input.Changes;
            ReadOnlyMemory<RawInputRecord> current = input.Current;
            ulong fence = input.FenceGeneration;
            int count = input.Count;
            bool indexed = input.TryGet(0, out RawInputRecord record);
            bool foundByName = input.TryFind("ExternalAction", RawInputPhase.Performed, out record);
            bool foundById = input.TryFind(activeMap, RawInputPhase.Performed, out record);
            ulong sequence = record.Sequence;
            double inputTime = record.InputTime;
        }

        private sealed class ExternalMemory : CoCoActivationMemory { }
    }

    internal sealed class ExternalInboxFilter : ICoCoEventInboxAdapter<int>
    {
        public bool Accept(in int payload) => payload != 0;
    }

    internal sealed class ExternalCondition : ICoCoStateConditionEvaluator
    {
        bool ICoCoStateConditionEvaluator.Evaluate(CoCoConditionEvaluationContext context) =>
            context.Intents.TryFirst<InputIntentData>(out _);
    }

    internal interface IExternalSection : ICoCoOperationSection { }

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

    internal struct ExternalInputReducer : ICoCoIntentReducer<InputIntentData>
    {
        InputIntentData ICoCoIntentReducer<InputIntentData>.Reduce(in InputIntentData current, in InputIntentData candidate)
            => candidate;
    }

    internal sealed class ExternalInputFactory : ICoCoIntentReducerFactory<InputIntentData, ExternalInputReducer>
    {
        ExternalInputReducer ICoCoIntentReducerFactory<InputIntentData, ExternalInputReducer>.Create(
            CoCoGraphInstanceId graphInstanceId) => default;
    }

    internal sealed class ExternalOperator : ICoCoOperator
    {
        CoCoOperatorDescriptor ICoCoOperator.Descriptor => throw new NotSupportedException();

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

        // D3.1 verifies the released wiring until P003 supplies the type-based replacement.
        internal static void RegisterReleasedInput(CoCoGraphDescriptorCatalogBuilder catalog,
            CoCoStateGraphHostBindingBuilder binding, CoCoIntentId id)
        {
            var token = new CoCoIntentReducerFactoryToken<InputIntentData, ExternalInputReducer, ExternalInputFactory>(1);
            bool catalogRegistered = catalog.TryRegisterIntent(id, 1, token, out _);
            bool hostRegistered = binding.TryRegisterIntent<InputIntentData, ExternalInputReducer, ExternalInputFactory>(
                id, new ExternalInputFactory(), 1, out var handle, out _);
            bool created = CoCoIntentSourceRequirement<InputIntentData>.TryCreate(handle, 1, out var requirement);
            bool bound = binding.TryBindIntentSource(0, requirement, out _);
        }

        internal static void Provider(ICoCoStateGraphProjectBindingProvider provider,
            CoCoStateGraphHostBindingBuilder binding, IReadOnlyList<Assembly> assemblies)
        {
            CoCoGraphDescriptorCatalog catalog = provider.Catalog;
            bool configured = provider.TryConfigure(binding, out _);
            bool installed = CoCoStateGraphProjectBindings.TryInstall(provider, out _);
            CoCoCompiledStateGraph graph = binding.Graph;
            CoCoStandardBindingProvider standard = CoCoStandardBindingProvider.Build(assemblies);
            CoCoStandardBindingProvider discovered = CoCoStandardBindingProvider.BuildForAssemblyOf<ExternalState>();
        }
    }
}
