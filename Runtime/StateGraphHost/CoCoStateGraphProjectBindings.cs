using System;
using System.Collections.Generic;
using UnityEngine;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>
    /// Project-owned, AOT-safe binding source. Install one immutable provider before any Host starts.
    /// </summary>
    public interface ICoCoStateGraphProjectBindingProvider
    {
        CoCoGraphDescriptorCatalog Catalog { get; }

        bool TryConfigure(
            CoCoStateGraphHostBindingBuilder builder,
            out CoCoDiagnostic diagnostic);
    }

    public static class CoCoStateGraphProjectBindings
    {
        private static ICoCoStateGraphProjectBindingProvider _provider;

        public static bool IsInstalled => _provider != null;

        public static bool TryInstall(
            ICoCoStateGraphProjectBindingProvider provider,
            out CoCoDiagnostic diagnostic)
        {
            if (provider == null || provider.Catalog == null || !provider.Catalog.IsFrozen)
            {
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.RegistryNotFrozen,
                    "StateGraph project bindings require one provider with a frozen descriptor Catalog.");
                return false;
            }

            if (_provider != null)
            {
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.RegistryFrozen,
                    "StateGraph project bindings are immutable after the first installation.");
                return false;
            }

            _provider = provider;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal static ICoCoStateGraphProjectBindingProvider Provider => _provider;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetAtSubsystemRegistration()
        {
            _provider = null;
            CoCoStateGraphTransactionCoordinatorRegistry.Reset();
            CoCoStateGraphEventRouterRegistry.Reset();
            CoCoStateGraphHostIdentity.Reset();
        }

        internal static void ResetForTests()
        {
            ResetAtSubsystemRegistration();
        }

        private static CoCoDiagnostic RegistryError(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Registry, code, message);
    }

    internal static class CoCoStateGraphHostBindingValidation
    {
        internal static bool TryValidate(
            CoCoCompiledStateGraph graph,
            ICoCoStateGraphProjectBindingProvider provider,
            int eventLaneCapacity,
            int eventSourceCapacity,
            int eventDedupCapacity,
            out CoCoDiagnostic diagnostic)
        {
            if (graph == null || provider == null ||
                !CoCoGraphInstanceId.TryCreate(
                    ulong.MaxValue,
                    out CoCoGraphInstanceId graphInstanceId))
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Registry,
                    CoCoDiagnosticCode.MissingDescriptor,
                    "Binding validation requires one compiled Graph and project provider.");
                return false;
            }

            var builder = new CoCoStateGraphHostBindingBuilder(graph, graphInstanceId);
            CoCoStateGraphHostRuntimeBindings bindings = null;
            try
            {
                if (!provider.TryConfigure(builder, out diagnostic) ||
                    diagnostic.IsError ||
                    !builder.TryFreeze(
                        eventLaneCapacity,
                        eventSourceCapacity,
                        eventDedupCapacity,
                        out bindings,
                        out diagnostic))
                {
                    builder.Abandon();
                    if (!diagnostic.IsError)
                    {
                        diagnostic = CoCoDiagnostic.Error(
                            CoCoDiagnosticDomain.Registry,
                            CoCoDiagnosticCode.MissingDescriptor,
                            "Project StateGraph bindings did not exactly cover the compiled Graph.");
                    }

                    return false;
                }

                bindings.Dispose();
                diagnostic = CoCoDiagnostic.None;
                return true;
            }
            catch (Exception)
            {
                if (bindings == null)
                {
                    builder.Abandon();
                }
                else
                {
                    bindings.Dispose();
                }

                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Registry,
                    CoCoDiagnosticCode.CommitPreparationFailed,
                    "Project StateGraph binding validation threw during temporary setup.");
                return false;
            }
        }
    }

    /// <summary>
    /// Per-Graph setup builder. All generic calls are made by project code so IL2CPP never needs
    /// reflection to construct State, Condition, Intent, Adapter, or Operation factories.
    /// </summary>
    public sealed class CoCoStateGraphHostBindingBuilder
    {
        private readonly CoCoCompiledStateGraph _graph;
        private readonly CoCoGraphInstanceId _graphInstanceId;
        private readonly CoCoStateGraphLogicBindingsBuilder _logic;
        private readonly CoCoOperationSectionRegistryBuilder _operations;
        private readonly CoCoIntentFrameLayout _intentLayout;
        private readonly bool[] _registeredIntents;
        private readonly int[] _intentProducerCounts;
        private readonly bool[] _boundEventDeclarations;
        private readonly List<CoCoOperationSectionRequirement> _operationRequirements;
        private readonly List<ICoCoHostIntentContribution> _intentContributions;
        private readonly List<ICoCoHostEventLane> _eventLanes;
        private CoCoIntentFrameRuntime _intentRuntime;
        private CoCoDiagnostic _setupFailure;
        private bool _intentBindingsBegun;
        private bool _isFrozen;

        internal CoCoStateGraphHostBindingBuilder(
            CoCoCompiledStateGraph graph,
            CoCoGraphInstanceId graphInstanceId)
        {
            _graph = graph ?? throw new ArgumentNullException(nameof(graph));
            _graphInstanceId = graphInstanceId;
            _logic = new CoCoStateGraphLogicBindingsBuilder(graph);
            _operations = new CoCoOperationSectionRegistryBuilder();
            _intentLayout = new CoCoIntentFrameLayout(
                graph.IntentRequirements.LayoutId,
                graph.IntentRequirements.Count);
            _registeredIntents = new bool[graph.IntentRequirements.Count];
            _intentProducerCounts = new int[graph.IntentRequirements.Count];
            _boundEventDeclarations = new bool[graph.IntentRequirements.AdapterCount];
            _operationRequirements = new List<CoCoOperationSectionRequirement>(
                graph.OperationProvides.Count);
            _intentContributions = new List<ICoCoHostIntentContribution>();
            _eventLanes = new List<ICoCoHostEventLane>();
        }

        public CoCoCompiledStateGraph Graph => _graph;
        public CoCoGraphInstanceId GraphInstanceId => _graphInstanceId;
        public bool IsFrozen => _isFrozen;

        public bool TryBindState(
            CoCoStateDescriptorId descriptorId,
            ICoCoStateRuntimeFactory factory,
            out CoCoDiagnostic diagnostic)
        {
            bool succeeded = _isFrozen
                ? FrozenFailure(out diagnostic)
                : _logic.TryBindState(descriptorId, factory, out diagnostic);
            return succeeded || LatchFailure(diagnostic);
        }

        public bool TryBindCondition(
            CoCoConditionDescriptorId descriptorId,
            ICoCoConditionRuntimeFactory factory,
            out CoCoDiagnostic diagnostic)
        {
            bool succeeded = _isFrozen
                ? FrozenFailure(out diagnostic)
                : _logic.TryBindCondition(descriptorId, factory, out diagnostic);
            return succeeded || LatchFailure(diagnostic);
        }

        public bool TryRegisterIntent<TIntent, TReducer, TFactory>(
            CoCoIntentId intentId,
            TFactory reducerFactory,
            ulong semanticFingerprint,
            out CoCoIntentHandle<TIntent> handle,
            out CoCoDiagnostic diagnostic)
            where TIntent : unmanaged
            where TReducer : unmanaged, ICoCoIntentReducer<TIntent>
            where TFactory : ICoCoIntentReducerFactory<TIntent, TReducer>
        {
            handle = default;
            if (_isFrozen || _intentBindingsBegun)
            {
                diagnostic = RegistryFrozen("Intent declarations are already frozen.");
                return LatchFailure(diagnostic);
            }

            if (ReferenceEquals(reducerFactory, null) || semanticFingerprint == 0UL ||
                !TryFindIntent(intentId, out CoCoIntentRequirement requirement) ||
                requirement.DenseIndex != _intentLayout.Count ||
                requirement.ValueType != typeof(TIntent) ||
                requirement.ReducerType != typeof(TReducer) ||
                requirement.ReducerFactoryType != typeof(TFactory) ||
                requirement.ReducerFactorySemanticFingerprint != semanticFingerprint ||
                _registeredIntents[requirement.DenseIndex])
            {
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.DescriptorTypeMismatch,
                    "Intent reducer binding must exactly match the compiled Intent manifest and dense order.");
                return LatchFailure(diagnostic);
            }

            if (!_intentLayout.TryRegister(
                    intentId,
                    requirement.MaxContributions,
                    reducerFactory,
                    out handle,
                    out diagnostic))
            {
                return LatchFailure(diagnostic);
            }

            _registeredIntents[requirement.DenseIndex] = true;
            return true;
        }

        public bool TryBeginIntentBindings(int bindingCapacity, out CoCoDiagnostic diagnostic)
        {
            if (_isFrozen || _intentBindingsBegun || bindingCapacity < 0 ||
                _graph.IntentRequirements.Count == 0)
            {
                diagnostic = RegistryFrozen("Intent runtime bindings may begin exactly once.");
                return LatchFailure(diagnostic);
            }

            for (int index = 0; index < _registeredIntents.Length; index++)
            {
                if (!_registeredIntents[index])
                {
                    diagnostic = RegistryError(
                        CoCoDiagnosticCode.MissingIntentReducer,
                        "Every compiled Intent requires one exact reducer factory before binding sources.");
                    return LatchFailure(diagnostic);
                }
            }

            if (!_intentLayout.Freeze(out diagnostic) ||
                !_intentLayout.TryCreateRuntime(
                    _graphInstanceId,
                    bindingCapacity,
                    out _intentRuntime,
                    out diagnostic))
            {
                return LatchFailure(diagnostic);
            }

            _intentBindingsBegun = true;
            return true;
        }

        public bool TryBindIntentSource<TIntent>(
            CoCoIntentSourceRequirement<TIntent> requirement,
            ICoCoIntentFrameSource<TIntent> source,
            out CoCoDiagnostic diagnostic)
            where TIntent : unmanaged
        {
            if (!CanBindIntent(requirement.Handle.IntentId, typeof(TIntent), out int denseIndex, out diagnostic))
            {
                return LatchFailure(diagnostic);
            }

            if (!_intentRuntime.TryBindSource(
                    requirement,
                    source,
                    out CoCoIntentSourceBinding<TIntent> binding,
                    out diagnostic))
            {
                return LatchFailure(diagnostic);
            }

            _intentContributions.Add(new CoCoHostIntentSourceContribution<TIntent>(binding));
            _intentProducerCounts[denseIndex]++;
            return true;
        }

        public bool TryBindEventAdapter<TEvent, TIntent>(
            CoCoEventDomainId eventDomainId,
            CoCoEventTypeId eventTypeId,
            CoCoIntentSourceRequirement<TIntent> requirement,
            int projectionCapacity,
            bool allowSourceEcho,
            ICoCoEventToIntentAdapter<TEvent, TIntent> adapter,
            out CoCoDiagnostic diagnostic)
            where TEvent : unmanaged
            where TIntent : unmanaged
        {
            if (!CanBindIntent(requirement.Handle.IntentId, typeof(TIntent), out int intentIndex, out diagnostic) ||
                !TryFindEventDeclaration(
                    eventDomainId,
                    eventTypeId,
                    requirement.Handle.IntentId,
                    typeof(TEvent),
                    typeof(TIntent),
                    out int declarationIndex) ||
                _boundEventDeclarations[declarationIndex])
            {
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.DescriptorTypeMismatch,
                    "Event Adapter binding must exactly match one unbound compiled declaration.");
                return LatchFailure(diagnostic);
            }

            CoCoHostEventLane<TEvent> lane = GetOrCreateEventLane<TEvent>(
                eventDomainId,
                eventTypeId,
                projectionCapacity,
                allowSourceEcho,
                out diagnostic);
            if (lane == null)
            {
                return LatchFailure(diagnostic);
            }

            if (!_intentRuntime.TryBindEventAdapter(
                    eventDomainId,
                    eventTypeId,
                    requirement,
                    projectionCapacity,
                    adapter,
                    out CoCoEventToIntentBinding<TEvent, TIntent> binding,
                    out diagnostic))
            {
                return LatchFailure(diagnostic);
            }

            _intentContributions.Add(
                new CoCoHostEventIntentContribution<TEvent, TIntent>(lane, binding));
            _boundEventDeclarations[declarationIndex] = true;
            _intentProducerCounts[intentIndex]++;
            return true;
        }

        public bool TryRegisterOperation<TSection>(
            CoCoOperationSectionId sectionId,
            CoCoOperationSectionMode mode,
            ICoCoOperationSectionViewFactory<TSection> viewFactory,
            ulong semanticFingerprint,
            out CoCoOperationSectionRequirement requirement,
            out CoCoDiagnostic diagnostic)
            where TSection : class, ICoCoOperationSection
        {
            requirement = default;
            if (_isFrozen || semanticFingerprint == 0UL ||
                !TryFindOperation(sectionId, out CoCoGraphOperationProvideRequirement compiled) ||
                compiled.Mode != mode ||
                compiled.SectionType != typeof(TSection) ||
                compiled.ViewFactoryType != viewFactory?.GetType() ||
                compiled.ViewFactorySemanticFingerprint != semanticFingerprint)
            {
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.MissingOperationBinding,
                    "Operation factory must exactly match one compiled Section declaration.");
                return LatchFailure(diagnostic);
            }

            for (int index = 0; index < _operationRequirements.Count; index++)
            {
                if (_operationRequirements[index].SectionId == sectionId)
                {
                    diagnostic = RegistryError(
                        CoCoDiagnosticCode.DuplicateIdentifier,
                        "Each Operation Section may be bound exactly once.");
                    return LatchFailure(diagnostic);
                }
            }

            if (!_operations.TryRegister(
                    sectionId,
                    mode,
                    viewFactory,
                    out requirement,
                    out diagnostic) ||
                requirement.Shape.ShapeFingerprint != compiled.Shape.ShapeFingerprint)
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = RegistryError(
                        CoCoDiagnosticCode.DescriptorTypeMismatch,
                        "Operation Section shape does not match the compiled declaration.");
                }

                return LatchFailure(diagnostic);
            }

            _operationRequirements.Add(requirement);
            return true;
        }

        internal bool TryFreeze(
            int eventLaneCapacity,
            int maxEventSources,
            int eventDedupCapacity,
            out CoCoStateGraphHostRuntimeBindings bindings,
            out CoCoDiagnostic diagnostic)
        {
            bindings = null;
            if (_setupFailure.IsError)
            {
                _isFrozen = true;
                DisposeIntentRuntime();
                diagnostic = _setupFailure;
                return false;
            }

            if (_isFrozen)
            {
                diagnostic = RegistryFrozen("Host bindings may only be frozen once.");
                return false;
            }

            _isFrozen = true;
            if (!_logic.TryFreeze(out CoCoStateGraphLogicBindings logicBindings, out diagnostic))
            {
                DisposeIntentRuntime();
                return false;
            }

            if (_graph.IntentRequirements.Count > 0)
            {
                if (!_intentBindingsBegun || _intentRuntime == null)
                {
                    diagnostic = RegistryError(
                        CoCoDiagnosticCode.MissingIntentReducer,
                        "The project provider did not create the required Intent runtime.");
                    return false;
                }

                for (int index = 0; index < _intentProducerCounts.Length; index++)
                {
                    if (_intentProducerCounts[index] == 0)
                    {
                        diagnostic = RegistryError(
                            CoCoDiagnosticCode.MissingDescriptor,
                            "Every compiled Intent must have at least one declared Source or Event Adapter binding.");
                        DisposeIntentRuntime();
                        return false;
                    }
                }
            }

            for (int index = 0; index < _boundEventDeclarations.Length; index++)
            {
                if (!_boundEventDeclarations[index])
                {
                    diagnostic = RegistryError(
                        CoCoDiagnosticCode.MissingDescriptor,
                        "Event Adapter bindings must exactly cover the compiled declaration manifest.");
                    DisposeIntentRuntime();
                    return false;
                }
            }

            if (_intentRuntime != null && !_intentRuntime.FreezeBindings(out diagnostic))
            {
                DisposeIntentRuntime();
                return false;
            }

            if (_operationRequirements.Count != _graph.OperationProvides.Count ||
                !_operations.TryFreeze(
                    _graph.OperationProvides.LayoutId,
                    out CoCoOperationSectionRegistry operationRegistry,
                    out diagnostic) ||
                !CoCoOperationFrame.TryCreate(
                    operationRegistry,
                    _graphInstanceId,
                    _operationRequirements,
                    out CoCoOperationFrame operationFrame,
                    out diagnostic))
            {
                if (diagnostic.IsNone)
                {
                    diagnostic = RegistryError(
                        CoCoDiagnosticCode.MissingOperationBinding,
                        "Operation bindings must exactly cover the compiled Provides manifest.");
                }

                DisposeIntentRuntime();
                return false;
            }

            CoCoActorEventInboxCore inbox = null;
            if (_eventLanes.Count > 0)
            {
                if (eventLaneCapacity <= 0 || maxEventSources <= 0 || eventDedupCapacity <= 0)
                {
                    diagnostic = CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Mailbox,
                        CoCoDiagnosticCode.MailboxOverflow,
                        "Host Inbox capacities must be positive when the Graph declares Events.");
                    DisposeIntentRuntime();
                    return false;
                }

                CoCoEventDomainId domainId = _eventLanes[0].EventDomainId;
                inbox = new CoCoActorEventInboxCore(
                    _graphInstanceId,
                    domainId,
                    _eventLanes.Count,
                    maxEventSources,
                    eventDedupCapacity);
                for (int index = 0; index < _eventLanes.Count; index++)
                {
                    if (!_eventLanes[index].TryRegister(
                            inbox,
                            eventLaneCapacity,
                            out diagnostic))
                    {
                        inbox.Dispose();
                        DisposeIntentRuntime();
                        return false;
                    }
                }

                if (!inbox.TryBindIntentRuntime(_intentRuntime, out diagnostic) ||
                    !inbox.Start(out diagnostic))
                {
                    inbox.Dispose();
                    DisposeIntentRuntime();
                    return false;
                }
            }

            bindings = new CoCoStateGraphHostRuntimeBindings(
                logicBindings,
                operationFrame,
                _intentRuntime,
                inbox,
                _intentContributions.ToArray(),
                _eventLanes.ToArray());
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal void Abandon()
        {
            _isFrozen = true;
            DisposeIntentRuntime();
        }

        private bool CanBindIntent(
            CoCoIntentId intentId,
            Type intentType,
            out int denseIndex,
            out CoCoDiagnostic diagnostic)
        {
            if (_isFrozen || !_intentBindingsBegun || _intentRuntime == null ||
                !TryFindIntent(intentId, out CoCoIntentRequirement requirement) ||
                requirement.ValueType != intentType ||
                !_registeredIntents[requirement.DenseIndex])
            {
                denseIndex = -1;
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.InvalidIntentDescriptor,
                    "Intent Sources and Adapters require one exact registered Intent runtime.");
                return false;
            }

            denseIndex = requirement.DenseIndex;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private CoCoHostEventLane<TEvent> GetOrCreateEventLane<TEvent>(
            CoCoEventDomainId domainId,
            CoCoEventTypeId eventTypeId,
            int projectionCapacity,
            bool allowSourceEcho,
            out CoCoDiagnostic diagnostic)
            where TEvent : unmanaged
        {
            if (projectionCapacity <= 0)
            {
                diagnostic = RegistryError(
                    CoCoDiagnosticCode.InvalidIntentDescriptor,
                    "Event projection capacity must be positive.");
                return null;
            }

            for (int index = 0; index < _eventLanes.Count; index++)
            {
                ICoCoHostEventLane existing = _eventLanes[index];
                if (existing.EventTypeId != eventTypeId)
                {
                    continue;
                }

                var typed = existing as CoCoHostEventLane<TEvent>;
                if (typed == null || existing.EventDomainId != domainId ||
                    existing.AllowSourceEcho != allowSourceEcho)
                {
                    diagnostic = RegistryError(
                        CoCoDiagnosticCode.ManifestConflict,
                        "One EventType lane cannot mix payload types, domains, or source-echo policy.");
                    return null;
                }

                typed.RestrictCapacity(projectionCapacity);
                diagnostic = CoCoDiagnostic.None;
                return typed;
            }

            var lane = new CoCoHostEventLane<TEvent>(
                domainId,
                eventTypeId,
                projectionCapacity,
                allowSourceEcho);
            _eventLanes.Add(lane);
            diagnostic = CoCoDiagnostic.None;
            return lane;
        }

        private bool TryFindIntent(CoCoIntentId intentId, out CoCoIntentRequirement requirement)
        {
            IReadOnlyList<CoCoIntentRequirement> requirements = _graph.IntentRequirements.Requirements;
            for (int index = 0; index < requirements.Count; index++)
            {
                if (requirements[index].IntentId == intentId)
                {
                    requirement = requirements[index];
                    return true;
                }
            }

            requirement = null;
            return false;
        }

        private bool TryFindEventDeclaration(
            CoCoEventDomainId domainId,
            CoCoEventTypeId eventTypeId,
            CoCoIntentId intentId,
            Type eventType,
            Type intentType,
            out int declarationIndex)
        {
            IReadOnlyList<CoCoCompiledEventToIntentDeclaration> declarations =
                _graph.IntentRequirements.EventAdapterDeclarations;
            for (int index = 0; index < declarations.Count; index++)
            {
                CoCoCompiledEventToIntentDeclaration declaration = declarations[index];
                if (declaration.EventDomainId == domainId &&
                    declaration.EventTypeId == eventTypeId &&
                    declaration.ProvidedIntentId == intentId &&
                    declaration.EventPayloadType == eventType &&
                    declaration.ProvidedIntentType == intentType)
                {
                    declarationIndex = index;
                    return true;
                }
            }

            declarationIndex = -1;
            return false;
        }

        private bool TryFindOperation(
            CoCoOperationSectionId sectionId,
            out CoCoGraphOperationProvideRequirement requirement)
        {
            IReadOnlyList<CoCoGraphOperationProvideRequirement> provides =
                _graph.OperationProvides.Provides;
            for (int index = 0; index < provides.Count; index++)
            {
                if (provides[index].SectionId == sectionId)
                {
                    requirement = provides[index];
                    return true;
                }
            }

            requirement = null;
            return false;
        }

        private bool FrozenFailure(out CoCoDiagnostic diagnostic)
        {
            diagnostic = RegistryFrozen("Host binding builder is frozen.");
            return false;
        }

        private bool LatchFailure(CoCoDiagnostic diagnostic)
        {
            if (!_setupFailure.IsError)
            {
                _setupFailure = diagnostic.IsError
                    ? diagnostic
                    : RegistryError(
                        CoCoDiagnosticCode.ManifestConflict,
                        "A project binding setup call failed without an error diagnostic.");
            }

            return false;
        }

        private void DisposeIntentRuntime()
        {
            _intentRuntime?.Dispose();
            _intentRuntime = null;
        }

        private static CoCoDiagnostic RegistryFrozen(string message) =>
            RegistryError(CoCoDiagnosticCode.RegistryFrozen, message);

        private static CoCoDiagnostic RegistryError(CoCoDiagnosticCode code, string message) =>
            CoCoDiagnostic.Error(CoCoDiagnosticDomain.Registry, code, message);
    }

    internal sealed class CoCoStateGraphHostRuntimeBindings : IDisposable
    {
        private readonly ICoCoHostIntentContribution[] _intentContributions;
        private readonly ICoCoHostEventLane[] _eventLanes;
        private CoCoTickFrame _cachedIntentTick;
        private ICoCoIntentFrame _cachedIntentFrame;
        private bool _hasCachedIntentTick;

        public CoCoStateGraphHostRuntimeBindings(
            CoCoStateGraphLogicBindings logic,
            CoCoOperationFrame operations,
            CoCoIntentFrameRuntime intents,
            CoCoActorEventInboxCore inbox,
            ICoCoHostIntentContribution[] intentContributions,
            ICoCoHostEventLane[] eventLanes)
        {
            Logic = logic;
            Operations = operations;
            Intents = intents;
            Inbox = inbox;
            _intentContributions = intentContributions;
            _eventLanes = eventLanes;
        }

        public CoCoStateGraphLogicBindings Logic { get; }
        public CoCoOperationFrame Operations { get; }
        public CoCoIntentFrameRuntime Intents { get; }
        public CoCoActorEventInboxCore Inbox { get; }
        public bool HasEvents => _eventLanes.Length != 0;

        public bool TryCollectIntents(
            in CoCoTickFrame tickFrame,
            out ICoCoIntentFrame frame,
            out CoCoDiagnostic diagnostic)
        {
            if (_hasCachedIntentTick)
            {
                if (_cachedIntentTick == tickFrame)
                {
                    frame = _cachedIntentFrame;
                    diagnostic = CoCoDiagnostic.None;
                    return true;
                }

                frame = null;
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Lifecycle,
                    CoCoDiagnosticCode.InvalidLifecycleTransition,
                    "The unresolved Intent Tick must be retried or discarded before collecting a later Tick.");
                return false;
            }

            frame = null;
            if (Intents == null)
            {
                _cachedIntentTick = tickFrame;
                _cachedIntentFrame = null;
                _hasCachedIntentTick = true;
                diagnostic = CoCoDiagnostic.None;
                return true;
            }

            if (!CoCoStateFlowFrameHeader.TryCreate(
                    Intents.GraphInstanceId,
                    Intents.LayoutId,
                    CoCoStateFlowFrameKind.Intent,
                    tickFrame,
                    out CoCoStateFlowFrameHeader header))
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Frame,
                    CoCoDiagnosticCode.InvalidFrameLayout,
                    "IntentFrame header did not match the Host Tick and layout.");
                return false;
            }

            // Seal input before Intent collection begins; Inbox generation changes are forbidden
            // while its bound Intent runtime is collecting.
            if (Inbox != null && !Inbox.SealForTick(tickFrame))
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Mailbox,
                    CoCoDiagnosticCode.MailboxUnavailable,
                    "Inbox could not seal the next Tick batch.");
                return false;
            }

            if (!Intents.TryBegin(header, out diagnostic))
            {
                return false;
            }

            for (int index = 0; index < _intentContributions.Length; index++)
            {
                if (!_intentContributions[index].TryCollect(Intents, tickFrame))
                {
                    Intents.CancelCollection();
                    diagnostic = CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Intent,
                        CoCoDiagnosticCode.InvalidIntentContribution,
                        "An Intent Source or Event Adapter failed while collecting the Tick.");
                    return false;
                }
            }

            if (!Intents.TryFreeze(out diagnostic))
            {
                return false;
            }

            frame = Intents.Frame;
            _cachedIntentTick = tickFrame;
            _cachedIntentFrame = frame;
            _hasCachedIntentTick = true;
            return true;
        }

        public void ResolveIntentTick(in CoCoTickFrame tickFrame)
        {
            if (!_hasCachedIntentTick || _cachedIntentTick != tickFrame)
            {
                return;
            }

            _cachedIntentTick = default;
            _cachedIntentFrame = null;
            _hasCachedIntentTick = false;
        }

        public CoCoInboxEnqueueResult TryEnqueueLocal<TEvent>(in CoCoEventPacket<TEvent> packet)
            where TEvent : unmanaged
        {
            for (int index = 0; index < _eventLanes.Length; index++)
            {
                if (_eventLanes[index] is CoCoHostEventLane<TEvent> lane &&
                    lane.EventTypeId == packet.Envelope.EventTypeId)
                {
                    return lane.TryEnqueue(packet);
                }
            }

            return CoCoInboxEnqueueResult.UndeclaredEventType;
        }

        public bool RegisterRouter(CoCoStateGraphHost host)
        {
            for (int index = 0; index < _eventLanes.Length; index++)
            {
                if (_eventLanes[index].RegisterRouter(host))
                {
                    continue;
                }

                for (int rollback = index - 1; rollback >= 0; rollback--)
                {
                    _eventLanes[rollback].UnregisterRouter();
                }

                return false;
            }

            return true;
        }

        public void UnregisterRouter()
        {
            for (int index = _eventLanes.Length - 1; index >= 0; index--)
            {
                _eventLanes[index].UnregisterRouter();
            }
        }

        public void Dispose()
        {
            UnregisterRouter();
            _cachedIntentTick = default;
            _cachedIntentFrame = null;
            _hasCachedIntentTick = false;
            Inbox?.Dispose();
            Intents?.Dispose();
        }
    }

    internal interface ICoCoHostIntentContribution
    {
        bool TryCollect(CoCoIntentFrameRuntime runtime, in CoCoTickFrame tickFrame);
    }

    internal sealed class CoCoHostIntentSourceContribution<TIntent> : ICoCoHostIntentContribution
        where TIntent : unmanaged
    {
        private readonly CoCoIntentSourceBinding<TIntent> _binding;

        public CoCoHostIntentSourceContribution(CoCoIntentSourceBinding<TIntent> binding)
        {
            _binding = binding;
        }

        public bool TryCollect(CoCoIntentFrameRuntime runtime, in CoCoTickFrame tickFrame)
        {
            CoCoIntentSourceSampleResult result = runtime.TrySample(_binding, tickFrame);
            return result == CoCoIntentSourceSampleResult.Contributed ||
                   result == CoCoIntentSourceSampleResult.NoValue;
        }
    }

    internal sealed class CoCoHostEventIntentContribution<TEvent, TIntent> : ICoCoHostIntentContribution
        where TEvent : unmanaged
        where TIntent : unmanaged
    {
        private readonly CoCoHostEventLane<TEvent> _lane;
        private readonly CoCoEventToIntentBinding<TEvent, TIntent> _binding;

        public CoCoHostEventIntentContribution(
            CoCoHostEventLane<TEvent> lane,
            CoCoEventToIntentBinding<TEvent, TIntent> binding)
        {
            _lane = lane;
            _binding = binding;
        }

        public bool TryCollect(CoCoIntentFrameRuntime runtime, in CoCoTickFrame tickFrame)
        {
            if (!_lane.TryGetSealedBatch(out CoCoActorEventSealedBatch<TEvent> batch))
            {
                return true;
            }

            CoCoIntentEventProjectionResult result = runtime.TryProject(_binding, batch);
            return result == CoCoIntentEventProjectionResult.Contributed ||
                   result == CoCoIntentEventProjectionResult.NoValue;
        }
    }
}
