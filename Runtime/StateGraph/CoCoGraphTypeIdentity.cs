using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core
{
    // Fingerprints in published graphs and saves include CLR type and assembly names.
    // Keep the identity of types shipped before the assembly consolidation. Only the
    // original types have aliases. New types use their declaring assembly as usual.
    internal static class CoCoGraphTypeIdentity
    {
        internal static string AssemblyName(Type type)
        {
            while (type.HasElementType) type = type.GetElementType();
            while (type.IsNested) type = type.DeclaringType;
            while (type.HasElementType) type = type.GetElementType();
            while (type.DeclaringType != null) type = type.DeclaringType;
            string current = type.Assembly.GetName().Name;
            if (type.Namespace != "CoCoFlow.Runtime.Core") return current;
            string name = type.Name;
            switch (current)
            {
                case "CoCoFlow.Runtime.Core.StateGraph":
                    if (Contracts.Contains(name)) return "CoCoFlow.Runtime.Core.Contracts";
                    if (StateFlow.Contains(name)) return "CoCoFlow.Runtime.Core.StateFlow";
                    break;
                case "CoCoFlow.Runtime.Core":
                    if (Contracts.Contains(name)) return "CoCoFlow.Runtime.Core.Contracts";
                    break;
                case "CoCoFlow.Runtime.StateGraphHost":
                    if (Authoring.Contains(name)) return "CoCoFlow.Runtime.Core.StateGraphAuthoring";
                    if (name == "CoCoEntityIdentity" || name == "CoCoEntityContext" ||
                        name == "CoCoContextProviderAttribute" || name == "CoCoLifecycleContext")
                        return "CoCoFlow.Runtime.Core";
                    break;
            }
            return current;
        }

        internal static string FullName(Type type)
        {
            string name = type.FullName ?? type.Name;
            Type element = type;
            while (element.HasElementType) element = element.GetElementType();
            if (element.ContainsGenericParameters || !element.IsConstructedGenericType) return name;
            foreach (Type argument in element.GetGenericArguments())
            {
                // CLR FullName embeds the assembly-qualified name of each generic argument.
                var assembly = argument.Assembly.GetName();
                assembly.Name = AssemblyName(argument);
                name = name.Replace(argument.AssemblyQualifiedName, FullName(argument) + ", " + assembly.FullName);
            }
            return name;
        }

        private static readonly HashSet<string> Contracts = new HashSet<string>(StringComparer.Ordinal)
        {
            "CoCoActivationId", "CoCoActivationMemory", "CoCoClockDomainId", "CoCoCodecId",
            "CoCoConditionConfig", "CoCoCorrelationId", "CoCoDiagnostic", "CoCoDiagnosticCode",
            "CoCoDiagnosticDomain", "CoCoDiagnosticSeverity", "CoCoEventDomainId", "CoCoEventSequence",
            "CoCoEventTypeId", "CoCoExecutionSequence", "CoCoFixedString64", "CoCoFrameLayoutId",
            "CoCoGraphId", "CoCoGraphInstanceId", "CoCoId128Parser", "CoCoIntentConsumeAttribute",
            "CoCoIntentId", "CoCoIntentRegistrationAttribute", "CoCoLayerId", "CoCoOperationProvideAttribute",
            "CoCoOperationSectionId", "CoCoOperationSequence", "CoCoOperatorClaimId", "CoCoOperatorId",
            "CoCoRuntimeLifecycleState", "CoCoRuntimeLifecycleStateExtensions", "CoCoStableEntityId",
            "CoCoStateAttribute", "CoCoStateBlockId", "CoCoStateCondition", "CoCoStateConfig",
            "CoCoStateConfigAttribute", "CoCoStateId", "CoCoStateLog", "CoCoStateLogic",
            "CoCoStateMemoryAttribute", "CoCoStateSlotId", "CoCoTemporalEntityId", "CoCoTickFrame",
            "CoCoTimelineEpoch", "CoCoTimelineId", "CoCoTimelinePosition", "CoCoTimelineTick",
            "CoCoTransitionId", "ConditionAttribute", "ConfigAttribute", "ICoCoEventInboxAdapter`1",
            "InputIntentData", "IntentAttribute", "MemoryAttribute", "OperationAttribute", "RawInputPhase",
            "RawInputRecord",
        };

        private static readonly HashSet<string> StateFlow = new HashSet<string>(StringComparer.Ordinal)
        {
            "CoCoActorContextBindingDescriptor", "CoCoActorContextBindingDescriptorBuilder",
            "CoCoActorContextCaptureContext", "CoCoActorContextValueRequirement", "CoCoActorContextWriter",
            "CoCoActorEventEnvelope", "CoCoActorEventInboxCore", "CoCoActorEventInboxLane`1",
            "CoCoActorEventInboxState", "CoCoActorEventLaneHandle`1", "CoCoActorEventManifestEntry",
            "CoCoActorEventSealedBatch`1", "CoCoActorInboxCounters", "CoCoCodecDescriptor",
            "CoCoCommittedEventSource", "CoCoContextCodecRegistry", "CoCoContextCommitResult",
            "CoCoContextCommitStatus", "CoCoContextFrame", "CoCoContextFrameArena", "CoCoContextFrameLayout",
            "CoCoContextFrameLayoutBuilder", "CoCoContextFrameOrigin", "CoCoContextFrameOriginKind",
            "CoCoContextFrameReadView", "CoCoContextFrameStorage", "CoCoContextFrameWriter",
            "CoCoContextProjection", "CoCoContextProjectionCodec", "CoCoContextRestorePolicy",
            "CoCoContextRestoreReadView", "CoCoContextRevision", "CoCoContextValueCodecAdapter`1",
            "CoCoDerivedStateReadContext", "CoCoDerivedStateRebuilderAdapter`1", "CoCoEventDeliveryMode",
            "CoCoEventOutboxLaneFactory`1", "CoCoEventOutboxLane`1", "CoCoEventOutboxRequirement",
            "CoCoEventOutboxTarget", "CoCoEventOutboxWriteResult", "CoCoEventOutboxWriter",
            "CoCoEventPacket`1", "CoCoEventReliability", "CoCoFinalizedContextCommit",
            "CoCoFinalizedOperationFrame", "CoCoGeneratedOperationViewFactory`1",
            "CoCoGraphContextCaptureContext", "CoCoGraphStateRecord`1", "CoCoInboxDedupEntry",
            "CoCoInboxEnqueueResult", "CoCoInboxSourceEpoch", "CoCoIntentArbitrationLane`2",
            "CoCoIntentContributionResult", "CoCoIntentContribution`1", "CoCoIntentDefinition`2",
            "CoCoIntentDescriptor", "CoCoIntentFrame", "CoCoIntentFrameArbiter", "CoCoIntentFrameLayout",
            "CoCoIntentFrameRuntime", "CoCoIntentFrameSlot`1", "CoCoIntentHandle`1",
            "CoCoIntentPassThroughFactory`1", "CoCoIntentPassThrough`1", "CoCoIntentSourceBinding`1",
            "CoCoIntentSourceCursor", "CoCoIntentSourceRequirement`1", "CoCoIntentSourceSampleResult",
            "CoCoOperationFrame", "CoCoOperationFrameWriter", "CoCoOperationSectionAttribute",
            "CoCoOperationSectionBinary", "CoCoOperationSectionContract", "CoCoOperationSectionDescriptor",
            "CoCoOperationSectionEntryHeader", "CoCoOperationSectionEntry`1",
            "CoCoOperationSectionFieldDescriptor", "CoCoOperationSectionFieldShape",
            "CoCoOperationSectionField`1", "CoCoOperationSectionHandle`1", "CoCoOperationSectionMode",
            "CoCoOperationSectionReader", "CoCoOperationSectionRegistry",
            "CoCoOperationSectionRegistryBuilder", "CoCoOperationSectionRequirement",
            "CoCoOperationSectionShape", "CoCoOperationSectionShapeFingerprint",
            "CoCoOperationSectionViewContext`1", "CoCoOperationViewFactoryAttribute", "CoCoOperationWriteRank",
            "CoCoOperatorClaimRequirement", "CoCoOperatorClaimState", "CoCoOperatorClaimSuspendPolicy",
            "CoCoOperatorDefinitionAttribute", "CoCoOperatorDescriptor", "CoCoOperatorDescriptorBuilder",
            "CoCoOperatorExecutionContext", "CoCoOperatorOutcome", "CoCoOperatorOutcomeRequirement",
            "CoCoOperatorOutcomeStatus", "CoCoPreparedContextCommit", "CoCoProjectionRestoreSource",
            "CoCoStagedGraphReadView", "CoCoStagedOperationFrame", "CoCoStateBlockDescriptor",
            "CoCoStateBlockHandle", "CoCoStateBlockOwner", "CoCoStateEventInbox", "CoCoStateEventLane`1",
            "CoCoStateFlowBinary", "CoCoStateFlowFrameHeader", "CoCoStateFlowFrameIdentity",
            "CoCoStateFlowFrameKind", "CoCoStateFlowSchemaHash", "CoCoStateFlowTickOrder",
            "CoCoStateFlowTraceBuffer", "CoCoStateFlowTraceEntry", "CoCoStateFlowTraceFilter",
            "CoCoStateFlowTraceFrameReference", "CoCoStateFlowTraceKind", "CoCoStateFlowTransitionRole",
            "CoCoStateFlowTypeRules", "CoCoStateInboxEventAttribute", "CoCoStateSlotDescriptor",
            "CoCoStateSlot`1", "CoCoTemporalHistory", "CoCoTemporalHistoryEntryInfo", "CoCoTemporalSelection",
            "ICoCoActivationMemoryStateBinding`2", "ICoCoActorContextBinding", "ICoCoActorContextValueSink",
            "ICoCoActorEventInboxLane", "ICoCoCommittedEventPublisher", "ICoCoContextFrame",
            "ICoCoContextMaterializedReadSource", "ICoCoContextValueCodecAdapter", "ICoCoContextValueCodec`1",
            "ICoCoCursorIntentSource`1", "ICoCoDerivedStateRebuilderAdapter", "ICoCoDerivedStateRebuilder`1",
            "ICoCoEventOutboxLane", "ICoCoEventOutboxLaneFactory", "ICoCoEventOutboxLane`1",
            "ICoCoEventOutboxSink", "ICoCoGraphContextValueProducer`1", "ICoCoIntentArbitrationLane",
            "ICoCoIntentArbitrationLane`1", "ICoCoIntentDefinition", "ICoCoIntentFrame",
            "ICoCoIntentFrameSlot", "ICoCoIntentFrameSource`1", "ICoCoIntentReducerFactory`2",
            "ICoCoIntentReducer`1", "ICoCoOperationFrame", "ICoCoOperationSection",
            "ICoCoOperationSectionViewFactory`1", "ICoCoOperationSectionWriteTarget", "ICoCoOperator",
            "ICoCoOperatorOutcomeSink", "ICoCoStagedGraphReadSource", "ICoCoStateEventLane",
            "ICoCoStateFlowTrace", "IViewFactoryRegistration", "RawInputPassThroughReducer",
            "RawInputReducerFactory", "RawIntents", "ViewFactoryRegistration`1",
        };

        private static readonly HashSet<string> Authoring = new HashSet<string>(StringComparer.Ordinal)
        {
            "CoCoSerializedId128", "CoCoStateGraphAsset", "CoCoStateGraphAssetCompileResult",
            "CoCoStateGraphAssetCompiler", "CoCoStateGraphAssetSnapshot", "CoCoStateGraphAssetSnapshotBuilder",
            "CoCoStateGraphCompilationCache", "CoCoStateGraphCompilationCacheKey",
            "CoCoStateGraphConditionRecord", "CoCoStateGraphConfigFreezer", "CoCoStateGraphEditorLayout",
            "CoCoStateGraphEventAdapterDeclarationRecord", "CoCoStateGraphLayerRecord",
            "CoCoStateGraphMainThreadGuard", "CoCoStateGraphManagedReferenceInspection",
            "CoCoStateGraphManagedReferenceInspectionBridge", "CoCoStateGraphStateLayoutRecord",
            "CoCoStateGraphStateRecord", "CoCoStateGraphTransitionRecord", "ICoCoStateGraphCompilationCache",
        };
    }
}
