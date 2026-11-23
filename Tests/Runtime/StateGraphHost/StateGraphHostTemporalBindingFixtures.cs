using System;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.StateGraph;
using CoCoFlow.Runtime.StateGraphHost;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using UnityEngine;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    using StateGraphHost = global::CoCoFlow.Runtime.StateGraphHost.StateGraphHost;

    public enum TemporalRestoreFixtureFailure
    {
        None = 0,
        Reject = 1,
        Throw = 2,
        Destroy = 3
    }

    internal sealed class TemporalActorRestoreBinding :
        MonoBehaviour,
        IActorContextBinding,
        IContextRestoreBinding
    {
        private StateSlotId _slotId;
        private ActorContextBindingDescriptor _descriptor;

        public ActorContextBindingDescriptor Descriptor => _descriptor;
        public int Value { get; set; } = TemporalHostDefaults.ActorStateValue;
        public int CaptureCount { get; private set; }
        public int ApplyCount { get; private set; }
        public int PreviewCount { get; private set; }
        public int ConfirmCount { get; private set; }
        public int CancelCount { get; private set; }
        public int CorrectionCount { get; private set; }
        public int LastAppliedValue { get; private set; }
        public ContextRestoreApplyKind LastApplyKind { get; private set; }
        public TemporalFrameInfo LastSource { get; private set; }
        public TickFrame LastTargetTickFrame { get; private set; }
        public ContextRestoreReader EscapedReader { get; private set; }
        public TemporalRestoreFixtureFailure Failure { get; set; }
        public bool FailCaptureAfterWorldMutation { get; set; }
        public bool MutateBeforeFailure { get; set; }
        public Action<ContextRestoreApplyKind> ApplyCallback { get; set; }

        public void Configure(StateSlotId slotId)
        {
            _slotId = slotId;
            var builder = new ActorContextBindingDescriptorBuilder();
            if (!builder.TryProduce<int>(slotId, out CoCoDiagnostic produce))
            {
                throw new InvalidOperationException(produce.Message);
            }

            if (!builder.TryFreeze<TemporalActorRestoreBinding>(
                    TemporalHostDefaults.ActorStateFingerprint,
                    out _descriptor,
                    out CoCoDiagnostic freeze))
            {
                throw new InvalidOperationException(freeze.Message);
            }
        }

        public bool TryCapture(
            in ActorContextCaptureContext context,
            out CoCoDiagnostic diagnostic)
        {
            CaptureCount++;
            if (FailCaptureAfterWorldMutation)
            {
                transform.localPosition = new Vector3(Value, 2f, 3f);
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Context,
                    CoCoDiagnosticCode.ContextCaptureFailed,
                    "Temporal Actor fixture failed after mutating the Unity world.");
                return false;
            }

            if (!context.Writer.TryWrite(_slotId, Value))
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Context,
                    CoCoDiagnosticCode.ContextCaptureFailed,
                    "Temporal Actor fixture could not write its Actor Slot.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        public bool TryApply(
            in ContextRestoreBindingContext context,
            out CoCoDiagnostic diagnostic)
        {
            ApplyCount++;
            LastApplyKind = context.ApplyKind;
            LastSource = context.Source;
            LastTargetTickFrame = context.TargetTickFrame;
            EscapedReader = context.Reader;
            switch (context.ApplyKind)
            {
                case ContextRestoreApplyKind.Preview:
                    PreviewCount++;
                    break;
                case ContextRestoreApplyKind.Confirm:
                    ConfirmCount++;
                    break;
                case ContextRestoreApplyKind.Cancel:
                    CancelCount++;
                    break;
                case ContextRestoreApplyKind.Correction:
                    CorrectionCount++;
                    break;
            }

            if (!context.IsValid ||
                !context.Reader.TryRead(_slotId, out int restoredValue))
            {
                diagnostic = CoCoDiagnostic.Error(
                    CoCoDiagnosticDomain.Context,
                    CoCoDiagnosticCode.InvalidRestoreMetadata,
                    "Temporal Restore fixture could not read its Actor Slot.");
                return false;
            }

            ApplyCallback?.Invoke(context.ApplyKind);
            if (MutateBeforeFailure)
            {
                transform.localPosition = new Vector3(restoredValue, 2f, 3f);
            }

            switch (Failure)
            {
                case TemporalRestoreFixtureFailure.Reject:
                    diagnostic = CoCoDiagnostic.Error(
                        CoCoDiagnosticDomain.Context,
                        CoCoDiagnosticCode.InvalidRestoreMetadata,
                        "Temporal Restore fixture rejected the projection.");
                    return false;
                case TemporalRestoreFixtureFailure.Throw:
                    throw new InvalidOperationException(
                        "Temporal Restore fixture threw after projection began.");
                case TemporalRestoreFixtureFailure.Destroy:
                    DestroyImmediate(this);
                    diagnostic = CoCoDiagnostic.None;
                    return true;
            }

            Value = restoredValue;
            LastAppliedValue = restoredValue;
            transform.localPosition = new Vector3(restoredValue, 0f, 0f);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }
    }

    internal sealed class TemporalHostBindingProvider :
        IStateGraphProjectBindingProvider
    {
        private readonly TemporalHostTestIds _ids;
        private readonly IContextValueCodec<int> _actorCodec;

        internal TemporalHostBindingProvider(
            TemporalHostTestIds ids,
            bool withDurableProjection = false,
            IContextValueCodec<int> actorCodec = null)
        {
            _ids = ids;
            _actorCodec = actorCodec;
            Catalog = BuildCatalog(
                ids,
                withDurableProjection,
                actorCodec == null
                    ? default
                    : actorCodec.Descriptor);
        }

        public GraphDescriptorCatalog Catalog { get; }

        public bool TryConfigure(
            StateGraphHostBindingBuilder builder,
            out CoCoDiagnostic diagnostic)
        {
            var memoryBinding = new TemporalHostMemoryStateBinding();
            if (!builder.TryBindGraphStateSlot<
                    TemporalHostMemory,
                    int,
                    TemporalHostMemoryStateBinding>(
                    _ids.LayerId,
                    _ids.StateId,
                    _ids.GraphStateBlockId,
                    _ids.GraphStateSlotId,
                    TemporalHostDefaults.GraphState(_ids),
                    TemporalHostDefaults.GraphStateFingerprint,
                    memoryBinding,
                    out diagnostic))
            {
                return false;
            }

            bool actorBound = _actorCodec == null
                ? builder.TryBindContextSlot(
                    _ids.ActorStateBlockId,
                    _ids.ActorStateSlotId,
                    TemporalHostDefaults.ActorStateValue,
                    TemporalHostDefaults.ActorStateFingerprint,
                    out diagnostic)
                : builder.TryBindContextSlot(
                    _ids.ActorStateBlockId,
                    _ids.ActorStateSlotId,
                    TemporalHostDefaults.ActorStateValue,
                    TemporalHostDefaults.ActorStateFingerprint,
                    _actorCodec,
                    out diagnostic);
            if (!actorBound)
            {
                return false;
            }

            var stateFactory = new StateRuntimeFactory<TemporalHostLogic, TemporalHostMemory>(
                _ => new TemporalHostLogic(),
                () => new TemporalHostMemory(),
                (source, destination) => destination.Value = source.Value,
                memory => memory.Value = 0,
                TemporalHostLogic.GetMemoryFingerprint);
            return builder.TryBindState(
                _ids.StateDescriptorId,
                stateFactory,
                out diagnostic);
        }

        private static GraphDescriptorCatalog BuildCatalog(
            TemporalHostTestIds ids,
            bool withDurableProjection,
            CodecDescriptor actorCodec)
        {
            var builder = new GraphDescriptorCatalogBuilder();
            ContextProjection projection = withDurableProjection
                ? ContextProjection.Temporal |
                  ContextProjection.Durable
                : ContextProjection.Temporal;
            Ensure(builder.TryRegisterStateBlock(
                ids.GraphStateBlockId,
                StateBlockOwner.Graph,
                out CoCoDiagnostic graphBlock), graphBlock);
            Ensure(builder.TryRegisterStateSlot(
                ids.GraphStateBlockId,
                ids.GraphStateSlotId,
                projection,
                ContextRestorePolicy.Stored,
                TemporalHostDefaults.GraphState(ids),
                TemporalHostDefaults.GraphStateFingerprint,
                default,
                null,
                out CoCoDiagnostic graphSlot), graphSlot);
            Ensure(builder.TryRegisterStateBlock(
                ids.ActorStateBlockId,
                StateBlockOwner.Actor,
                out CoCoDiagnostic actorBlock), actorBlock);
            Ensure(builder.TryRegisterStateSlot(
                ids.ActorStateBlockId,
                ids.ActorStateSlotId,
                projection,
                ContextRestorePolicy.Stored,
                TemporalHostDefaults.ActorStateValue,
                TemporalHostDefaults.ActorStateFingerprint,
                actorCodec,
                null,
                out CoCoDiagnostic actorSlot), actorSlot);
            Ensure(builder.TryRegisterState(
                ids.StateDescriptorId,
                1U,
                new HostTestStateConfigFreezer(),
                new StateRuntimeRegistration<
                    TemporalHostLogic,
                    HostTestStateConfigSchema,
                    TemporalHostMemory>(HostTestSchemas.State, false),
                null,
                null,
                new[] { ids.GraphStateBlockId, ids.ActorStateBlockId },
                out CoCoDiagnostic state), state);
            Ensure(builder.TryFreeze(
                out GraphDescriptorCatalog catalog,
                out CoCoDiagnostic freeze), freeze);
            return catalog;
        }

        private static void Ensure(bool succeeded, CoCoDiagnostic diagnostic)
        {
            if (!succeeded)
            {
                throw new InvalidOperationException(diagnostic.Message);
            }
        }
    }

    internal sealed class TemporalHostTestScenario
    {
        internal bool WithEvent { get; set; }
        internal TemporalHostTestIds Ids { get; set; }
        internal TemporalHostBindingProvider Provider { get; set; }
        internal StateGraphAsset Asset { get; set; }
        internal GameObject GameObject { get; set; }
        internal StateGraphHost Host { get; set; }
        internal TemporalActorRestoreBinding Binding { get; set; }
    }

    internal static class TemporalHostTestHarness
    {
        internal static TemporalHostTestScenario Create(
            int historyCapacity,
            bool withEvent = false,
            bool assignRestoreBinding = true,
            bool withDurableProjection = false,
            IContextValueCodec<int> actorCodec = null)
        {
            TemporalHostTestIds ids = TemporalHostTestIds.Create();
            var provider = new TemporalHostBindingProvider(
                ids,
                withDurableProjection,
                actorCodec);
            if (!StateGraphProjectBindings.TryInstall(
                    provider,
                    out CoCoDiagnostic install))
            {
                throw new InvalidOperationException(install.Message);
            }

            StateGraphAsset asset = ScriptableObject.CreateInstance<StateGraphAsset>();
            asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
            var state = new StateGraphStateRecord(
                Serialize(ids.StateId),
                default,
                "Temporal Host State",
                Serialize(ids.StateDescriptorId),
                new HostTestStateConfig { Value = 1 });
            var layer = new StateGraphLayerRecord(Serialize(ids.LayerId), "Base");
            layer.InitialStateId = Serialize(ids.StateId);
            layer.States.Add(state);
            asset.Layers.Add(layer);
            var gameObject = new GameObject("Pre6 Temporal Host Test");
            StateGraphHost host = gameObject.AddComponent<StateGraphHost>();
            var binding = gameObject.AddComponent<TemporalActorRestoreBinding>();
            binding.Configure(ids.ActorStateSlotId);
            SetField(host, "stateGraphAsset", asset);
            SetField(host, "driver", StateGraphDriver.Manual);
            SetField(host, "autoStart", false);
            SetField(host, "contextFrameCapacity", 4);
            SetField(host, "eventLaneCapacity", 4);
            SetField(host, "eventOutboxCapacity", 4);
            SetField(host, "traceCapacity", 64);
            SetField(host, "temporalHistoryCapacity", historyCapacity);
            SetField(host, "actorContextBinding", binding);
            if (withEvent)
            {
                var adapter = gameObject.AddComponent<TemporalHostEventAdapterComponent>();
                SetField(host, "eventAdapters", new MonoBehaviour[] { adapter });
            }

            SetField(
                host,
                "contextRestoreBinding",
                assignRestoreBinding ? binding : null);
            return new TemporalHostTestScenario
            {
                WithEvent = withEvent,
                Ids = ids,
                Provider = provider,
                Asset = asset,
                GameObject = gameObject,
                Host = host,
                Binding = binding
            };
        }

        internal static TemporalHostTestScenario CreateSibling(
            TemporalHostTestScenario source,
            int historyCapacity)
        {
            if (source == null || source.Provider == null || source.Asset == null)
            {
                throw new ArgumentException("A live Temporal Host source scenario is required.", nameof(source));
            }

            var gameObject = new GameObject("Pre6 Temporal Host Sibling Test");
            StateGraphHost host = gameObject.AddComponent<StateGraphHost>();
            var binding = gameObject.AddComponent<TemporalActorRestoreBinding>();
            binding.Configure(source.Ids.ActorStateSlotId);
            SetField(host, "stateGraphAsset", source.Asset);
            SetField(host, "driver", StateGraphDriver.Manual);
            SetField(host, "autoStart", false);
            SetField(host, "contextFrameCapacity", 4);
            SetField(host, "eventLaneCapacity", 4);
            SetField(host, "eventOutboxCapacity", 4);
            SetField(host, "traceCapacity", 64);
            SetField(host, "temporalHistoryCapacity", historyCapacity);
            SetField(host, "actorContextBinding", binding);
            if (source.WithEvent)
            {
                var adapter = gameObject.AddComponent<TemporalHostEventAdapterComponent>();
                SetField(host, "eventAdapters", new MonoBehaviour[] { adapter });
            }

            SetField(host, "contextRestoreBinding", binding);
            return new TemporalHostTestScenario
            {
                WithEvent = source.WithEvent,
                Ids = source.Ids,
                Provider = source.Provider,
                Asset = source.Asset,
                GameObject = gameObject,
                Host = host,
                Binding = binding
            };
        }

        internal static StateGraphRuntime GetRuntime(StateGraphHost host)
        {
            FieldInfo field = typeof(StateGraphHost).GetField(
                "_runtime",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return (StateGraphRuntime)field?.GetValue(host);
        }

        internal static void SetRestoreBinding(
            StateGraphHost host,
            MonoBehaviour binding) =>
            SetField(host, "contextRestoreBinding", binding);

        internal static StateGraphHostRuntimeBindings GetBindings(
            StateGraphHost host)
        {
            FieldInfo field = typeof(StateGraphHost).GetField(
                "_bindings",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return (StateGraphHostRuntimeBindings)field?.GetValue(host);
        }

        internal static int ReadActorValue(
            ContextFrame frame,
            StateSlotId slotId)
        {
            if (!frame.Layout.TryResolveSlot(slotId, out StateSlot<int> slot))
            {
                throw new InvalidOperationException("Temporal Actor Slot could not be resolved.");
            }

            return frame.Read(slot);
        }

        internal static GraphStateRecord<int> ReadGraphState(
            ContextFrame frame,
            StateSlotId slotId)
        {
            if (!frame.Layout.TryResolveSlot(
                    slotId,
                    out StateSlot<GraphStateRecord<int>> slot))
            {
                throw new InvalidOperationException("Temporal Graph Slot could not be resolved.");
            }

            return frame.Read(slot);
        }

        internal static EventPacket<TemporalHostEvent> Packet(
            TemporalHostTestScenario scenario,
            ulong eventSequence,
            int value,
            TimelineEpoch sourceEpoch)
        {
            if (!EventSequence.TryCreate(
                    eventSequence,
                    out EventSequence sequence) ||
                !ActorEventEnvelope.TryCreate(
                    scenario.Ids.EventTypeId,
                    scenario.Ids.EventDomainId,
                    scenario.Host.GraphInstanceId,
                    scenario.Host.GraphInstanceId,
                    sourceEpoch,
                    new TimelineTick(1UL),
                    sequence,
                    EventDeliveryMode.Targeted,
                    EventReliability.Reliable,
                    default,
                    default,
                    default,
                    out ActorEventEnvelope envelope) ||
                !EventPacket<TemporalHostEvent>.TryCreate(
                    envelope,
                    new TemporalHostEvent { Value = value },
                    out EventPacket<TemporalHostEvent> packet))
            {
                throw new InvalidOperationException("Temporal Host Event packet is invalid.");
            }

            return packet;
        }

        private static void SetField<TValue>(
            StateGraphHost host,
            string fieldName,
            TValue value)
        {
            FieldInfo field = typeof(StateGraphHost).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(typeof(StateGraphHost).FullName, fieldName);
            }

            field.SetValue(host, value);
        }

        private static SerializedId128 Serialize(LayerId id) =>
            new SerializedId128(id.High, id.Low);

        private static SerializedId128 Serialize(StateId id) =>
            new SerializedId128(id.High, id.Low);

        private static SerializedId128 Serialize(StateDescriptorId id) =>
            new SerializedId128(id.High, id.Low);
    }
}
