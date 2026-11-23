using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.StateGraph
{
    // Graphs and saves hash type names. Keep the original identity of the package
    // modules on disk while exposing their current CLR identities to callers.
    internal static class GraphTypeIdentity
    {
        private const string Core = "CoCoFlow.Runtime.Core";
        private const string Graph = "CoCoFlow.Runtime.StateGraph";
        private const string Host = "CoCoFlow.Runtime.StateGraphHost";

        internal static string AssemblyName(Type type)
        {
            while (type.GetElementType() is { } element)
            {
                type = element;
            }
            while (type.DeclaringType is { } owner)
            {
                type = owner;
            }
            string current = type.Assembly.GetName().Name;
            if (type.Namespace != current) return current;
            string name = PublishedName(type, current);
            switch (current)
            {
                case Graph:
                    if (Contracts.Contains(name)) return "CoCoFlow.Runtime.Core.Contracts";
                    if (StateFlow.Contains(name)) return "CoCoFlow.Runtime.Core.StateFlow";
                    return "CoCoFlow.Runtime.Core.StateGraph";
                case Core:
                    if (Contracts.Contains(name)) return "CoCoFlow.Runtime.Core.Contracts";
                    break;
                case Host:
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
            while (element.GetElementType() is { } inner)
            {
                element = inner;
            }
            Type definition = element.IsGenericType ? element.GetGenericTypeDefinition() : element;
            string original = definition.FullName;
            if (original != null && name.StartsWith(original, StringComparison.Ordinal))
                name = PublishedDefinitionName(definition) + name.Substring(original.Length);
            if (element.ContainsGenericParameters || !element.IsConstructedGenericType) return name;
            foreach (Type argument in element.GetGenericArguments())
            {
                // CLR FullName embeds the assembly-qualified name of each generic argument.
                string qualifiedName = argument.AssemblyQualifiedName;
                if (qualifiedName == null) continue;
                var assembly = argument.Assembly.GetName();
                assembly.Name = AssemblyName(argument);
                name = name.Replace(qualifiedName, FullName(argument) + ", " + assembly.FullName);
            }
            return name;
        }

        private static string PublishedDefinitionName(Type type)
        {
            if (type.DeclaringType is { } parent)
                return PublishedDefinitionName(parent) + "+" + type.Name;

            string assembly = type.Assembly.GetName().Name;
            string ns = type.Namespace;
            if (ns != assembly) return type.FullName ?? type.Name;
            string publishedNamespace = ns == Graph || ns == Host ? Core : ns;
            return publishedNamespace + "." + PublishedName(type, assembly);
        }

        // A fixed lookup keeps released names stable without assigning old names to new gameplay types.
        private static string PublishedName(Type type, string assembly)
        {
            string name = type.Name;
            if (type.Namespace != assembly || assembly == Core) return name;
            string previous = type.IsInterface && name.StartsWith("I", StringComparison.Ordinal)
                ? "ICoCo" + name.Substring(1) : "CoCo" + name;
            bool renamed;
            switch (assembly)
            {
                case Graph:
                    renamed = Contracts.Contains(previous) || StateFlow.Contains(previous) || GraphRenames.Contains(previous);
                    break;
                case Host:
                    renamed = Authoring.Contains(previous) || HostRenames.Contains(previous);
                    break;
                default:
                    renamed = OtherModuleRenames.Contains(assembly + "." + previous);
                    break;
            }
            return renamed ? previous : name;
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
        private static readonly HashSet<string> GraphRenames = new HashSet<string>(StringComparer.Ordinal)
        {
            "CoCoActivePath", "CoCoActorClock", "CoCoAnyStateLogic", "CoCoCompiledCondition",
            "CoCoCompiledEventToIntentDeclaration", "CoCoCompiledState", "CoCoCompiledStateGraph",
            "CoCoCompiledStateLayer", "CoCoCompiledTransition", "CoCoConditionDescriptor",
            "CoCoConditionDescriptorId", "CoCoConditionDescriptorRegistration`3",
            "CoCoConditionEvaluationContext", "CoCoConditionFactoryContext", "CoCoConditionRuntimeFactory`1",
            "CoCoConditionRuntimeRegistration", "CoCoConditionRuntimeRegistration`2", "CoCoConditionSource",
            "CoCoContextFrameStateRequirementManifest", "CoCoContextStateBlockRequirement",
            "CoCoContextStateSlotRequirement", "CoCoDerivedStateRebuilderToken`2", "CoCoEmptyConditionConfig",
            "CoCoEventToIntentDeclarationKey", "CoCoEventToIntentDeclarationSource", "CoCoFieldConfig",
            "CoCoFieldConfigSchema", "CoCoFieldMemoryTransfer`1", "CoCoFieldMemory`2", "CoCoFrozenArray`1",
            "CoCoFrozenConfigArrayField`2", "CoCoFrozenConfigFieldDefinition", "CoCoFrozenConfigFieldId",
            "CoCoFrozenConfigField`2", "CoCoFrozenConfigHash", "CoCoFrozenConfigSchemaBuilder`1",
            "CoCoFrozenConfigSchema`1", "CoCoFrozenConfigSnapshot", "CoCoFrozenConfigValueContract",
            "CoCoFrozenConfigWriter`1", "CoCoGraphAuthorAssemblyValidator", "CoCoGraphCatalogHash",
            "CoCoGraphConfigTypeValidator", "CoCoGraphDescriptorCatalog", "CoCoGraphDescriptorCatalogBuilder",
            "CoCoGraphDescriptorIdParser", "CoCoGraphDiagnostic", "CoCoGraphDiagnosticLocation",
            "CoCoGraphElementKind", "CoCoGraphEventToIntentDeclarationRegistration", "CoCoGraphField",
            "CoCoGraphIntentRegistration`2", "CoCoGraphLayoutIdentity", "CoCoGraphOperationProvision",
            "CoCoGraphOperationProvisionManifest", "CoCoGraphOperationRegistration`1",
            "CoCoGraphSourceCollections", "CoCoGraphStateBlockRegistration",
            "CoCoGraphStateSlotRegistration`1", "CoCoGraphTypeIdentity", "CoCoIntentReducerFactoryToken`3",
            "CoCoIntentRequirement", "CoCoIntentRequirementManifest", "CoCoOperationSectionViewFactoryToken`2",
            "CoCoPreparedActorClockRestore", "CoCoPreparedGraphCommit", "CoCoPreparedGraphRestore",
            "CoCoPreparedTemporalRecovery", "CoCoRuntimeFault", "CoCoStagedGraphStep",
            "CoCoStateCallbackOperationLease", "CoCoStateDescriptor", "CoCoStateDescriptorId",
            "CoCoStateDescriptorRegistration`4", "CoCoStateEventBatch`1", "CoCoStateEventOutboxWriter",
            "CoCoStateEventReadView", "CoCoStateExecutionContext", "CoCoStateFactoryContext",
            "CoCoStateFields", "CoCoStateGraphCommittedDebugActiveState", "CoCoStateGraphCommittedDebugLayer",
            "CoCoStateGraphCommittedDebugState", "CoCoStateGraphCompileResult", "CoCoStateGraphCompiler",
            "CoCoStateGraphLogicBindings", "CoCoStateGraphLogicBindingsBuilder", "CoCoStateGraphRestoreState",
            "CoCoStateGraphRuntime", "CoCoStateGraphSource", "CoCoStateLayerSource",
            "CoCoStateOperationWriter", "CoCoStatePropertyBindings", "CoCoStateRuntimeFactory`2",
            "CoCoStateRuntimeRegistration", "CoCoStateRuntimeRegistration`3", "CoCoStateSource",
            "CoCoTransitionHandle", "CoCoTransitionSource", "CoCoTransitionWindow", "CoCoTransitionWindowMode",
            "ICoCoConditionDescriptorRegistration", "ICoCoConditionRuntimeFactory", "ICoCoConfigFreezer`2",
            "ICoCoFieldMemory", "ICoCoFrozenArrayValue", "ICoCoFrozenConfigSchema",
            "ICoCoGraphEventToIntentDeclarationRegistration", "ICoCoGraphIntentRegistration",
            "ICoCoGraphOperationRegistration", "ICoCoGraphStateSlotRegistration",
            "ICoCoStateConditionEvaluator", "ICoCoStateDescriptorRegistration", "ICoCoStateEnter",
            "ICoCoStateEventOutbox", "ICoCoStateExit", "ICoCoStateGraphCommitGuard",
            "ICoCoStateGraphContextRuntime", "ICoCoStateRuntimeFactory", "ICoCoStateUpdate",
        };

        private static readonly HashSet<string> HostRenames = new HashSet<string>(StringComparer.Ordinal)
        {
            "CoCoClaimRestoreOverlayPolicy", "CoCoContextProviderAttribute", "CoCoContextRestoreApplyKind",
            "CoCoContextRestoreBindingContext", "CoCoContextRestoreReadLease", "CoCoContextRestoreReader",
            "CoCoCoreUnityIntegration", "CoCoEntityContext", "CoCoEntityIdentity", "CoCoFlowRuntimeBootstrap",
            "CoCoGraphClaimContextBinding", "CoCoGraphContextProducerKind", "CoCoGraphStateContextBinding`3",
            "CoCoGraphValueContextBinding`2", "CoCoHostIntentSourceContribution`1",
            "CoCoLegacyEmptyConditionConfig", "CoCoLegacyEmptyStateConfig", "CoCoLegacyFieldConfig",
            "CoCoLegacyHostStateRecord", "CoCoLifecycleContext", "CoCoPreparedActorRestore",
            "CoCoProducerFingerprintExtensions", "CoCoStandardBindingProvider",
            "CoCoStandardIntentRegistration`3", "CoCoStandardOperationRegistration",
            "CoCoStandardOperatorBuilder", "CoCoStandardOperatorDefinition", "CoCoStandardSingleIntent",
            "CoCoStandardTypeIdentity", "CoCoStateGraphContextRuntime", "CoCoStateGraphDriver",
            "CoCoStateGraphHost", "CoCoStateGraphHostBindingBuilder", "CoCoStateGraphHostBoundary",
            "CoCoStateGraphHostDebugActiveState", "CoCoStateGraphHostDebugLayer",
            "CoCoStateGraphHostDebugSnapshot", "CoCoStateGraphHostIdentity",
            "CoCoStateGraphHostRuntimeBindings", "CoCoStateGraphHostTemporalDebugSnapshot",
            "CoCoStateGraphOperatorRuntime", "CoCoStateGraphPersistenceEnvelope",
            "CoCoStateGraphPersistencePayloadCodec", "CoCoStateGraphProjectBindings",
            "CoCoStateGraphTemporalController", "CoCoStateGraphTransaction", "CoCoStateLogInstaller",
            "CoCoTemporalDecoratorChain", "CoCoTemporalFrameInfo", "CoCoTemporalMode", "CoCoTemporalState",
            "ICoCoContextRestoreBinding", "ICoCoContextRestoreReadSource", "ICoCoGraphContextProducerBinding",
            "ICoCoGraphStateContextBinding", "ICoCoGraphValueContextBinding", "ICoCoHostIntentContribution",
            "ICoCoLegacyGraphConfig`1", "ICoCoPersistenceRestoreOperator", "ICoCoStandardIntentRegistration",
            "ICoCoStandardIntentSourceBinding", "ICoCoStateGraphProjectBindingProvider",
            "ICoCoStateGraphTemporalParticipant", "ICoCoTemporalDecoratorBinding",
            "ICoCoTemporalRestoreOperator",
        };

        private static readonly HashSet<string> OtherModuleRenames = new HashSet<string>(StringComparer.Ordinal)
        {
            "CoCoFlow.Runtime.Content.CoCoContentHost",
            "CoCoFlow.Runtime.Modules.Map.CoCoDefaultRegionCatalogProvider",
            "CoCoFlow.Runtime.Modules.Map.CoCoMapHost", "CoCoFlow.Runtime.Modules.Map.CoCoRegionBinding",
            "CoCoFlow.Runtime.Modules.Map.CoCoRegionChunkAnchor",
            "CoCoFlow.Runtime.Modules.Map.CoCoRegionProfile",
            "CoCoFlow.Runtime.Modules.Map.Temporal.CoCoMapTemporalBinding",
            "CoCoFlow.Runtime.Modules.Persistence.CoCoPersistedFrameDebugInfo",
            "CoCoFlow.Runtime.Modules.Persistence.CoCoStateGraphPersistenceDebugReader",
            "CoCoFlow.Runtime.Pooling.CoCoPoolHost",
            "CoCoFlow.Runtime.Pooling.Temporal.CoCoPoolTemporalBinding",
        };

    }
}
