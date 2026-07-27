using System;
using System.Reflection;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Tests.Runtime.StateGraphHost.Fixtures;
using UnityEngine;

namespace CoCoFlow.Tests.Runtime.StateGraphHost
{
    public enum TemporalRestoreFixtureFailure
    {
        None = 0,
        Reject = 1,
        Throw = 2,
        Destroy = 3
    }

    internal sealed class TemporalActorRestoreBinding :
        MonoBehaviour,
        ICoCoActorContextBinding,
        ICoCoContextRestoreBinding
    {
        private CoCoStateSlotId _slotId;
        private CoCoActorContextBindingDescriptor _descriptor;

        public CoCoActorContextBindingDescriptor Descriptor => _descriptor;
        public int Value { get; set; } = TemporalHostDefaults.ActorStateValue;
        public int CaptureCount { get; private set; }
        public int ApplyCount { get; private set; }
        public int PreviewCount { get; private set; }
        public int ConfirmCount { get; private set; }
        public int CancelCount { get; private set; }
        public int CorrectionCount { get; private set; }
        public int LastAppliedValue { get; private set; }
        public CoCoContextRestoreApplyKind LastApplyKind { get; private set; }
        public CoCoTemporalFrameInfo LastSource { get; private set; }
        public CoCoTickFrame LastTargetTickFrame { get; private set; }
        public CoCoContextRestoreReader EscapedReader { get; private set; }
        public TemporalRestoreFixtureFailure Failure { get; set; }
        public bool FailCaptureAfterWorldMutation { get; set; }
        public bool MutateBeforeFailure { get; set; }
        public Action<CoCoContextRestoreApplyKind> ApplyCallback { get; set; }

        public void Configure(CoCoStateSlotId slotId)
        {
            _slotId = slotId;
            var builder = new CoCoActorContextBindingDescriptorBuilder();
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
            in CoCoActorContextCaptureContext context,
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
            in CoCoContextRestoreBindingContext context,
            out CoCoDiagnostic diagnostic)
        {
            ApplyCount++;
            LastApplyKind = context.ApplyKind;
            LastSource = context.Source;
            LastTargetTickFrame = context.TargetTickFrame;
            EscapedReader = context.Reader;
            switch (context.ApplyKind)
            {
                case CoCoContextRestoreApplyKind.Preview:
                    PreviewCount++;
                    break;
                case CoCoContextRestoreApplyKind.Confirm:
                    ConfirmCount++;
                    break;
                case CoCoContextRestoreApplyKind.Cancel:
                    CancelCount++;
                    break;
                case CoCoContextRestoreApplyKind.Correction:
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
        ICoCoStateGraphProjectBindingProvider
    {
        private readonly TemporalHostTestIds _ids;
        private readonly bool _withEvent;
        private readonly ICoCoContextValueCodec<int> _actorCodec;

        internal TemporalHostBindingProvider(
            TemporalHostTestIds ids,
            bool withEvent,
            bool withDurableProjection = false,
            ICoCoContextValueCodec<int> actorCodec = null)
        {
            _ids = ids;
            _withEvent = withEvent;
            _actorCodec = actorCodec;
            Catalog = BuildCatalog(
                ids,
                withEvent,
                withDurableProjection,
                actorCodec == null
                    ? default
                    : actorCodec.Descriptor);
        }

        public CoCoGraphDescriptorCatalog Catalog { get; }

        public bool TryConfigure(
            CoCoStateGraphHostBindingBuilder builder,
            out CoCoDiagnostic diagnostic)
        {
            CoCoIntentHandle<TemporalHostIntent> intent = default;
            if (_withEvent)
            {
                if (!builder.TryRegisterIntent<
                        TemporalHostIntent,
                        TemporalHostIntentReducer,
                        TemporalHostIntentReducerFactory>(
                        _ids.IntentId,
                        new TemporalHostIntentReducerFactory(),
                        TemporalHostDefaults.IntentReducerFingerprint,
                        out intent,
                        out diagnostic) ||
                    !builder.TryBeginIntentBindings(out diagnostic) ||
                    !CoCoIntentSourceRequirement<TemporalHostIntent>.TryCreate(
                        intent,
                        1,
                        out CoCoIntentSourceRequirement<TemporalHostIntent> requirement) ||
                    !builder.TryBindEventAdapter<TemporalHostEvent, TemporalHostIntent>(
                        0,
                        _ids.EventDomainId,
                        _ids.EventTypeId,
                        requirement,
                        4,
                        false,
                        out diagnostic))
                {
                    return false;
                }
            }

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

            var stateFactory = new CoCoStateRuntimeFactory<TemporalHostLogic, TemporalHostMemory>(
                context => new TemporalHostLogic(context.GraphInstanceId, intent),
                () => new TemporalHostMemory(),
                (source, destination) => destination.Value = source.Value,
                memory => memory.Value = 0,
                TemporalHostLogic.GetMemoryFingerprint);
            return builder.TryBindState(
                _ids.StateDescriptorId,
                stateFactory,
                out diagnostic);
        }

        private static CoCoGraphDescriptorCatalog BuildCatalog(
            TemporalHostTestIds ids,
            bool withEvent,
            bool withDurableProjection,
            CoCoCodecDescriptor actorCodec)
        {
            var builder = new CoCoGraphDescriptorCatalogBuilder();
            CoCoContextProjection projection = withDurableProjection
                ? CoCoContextProjection.Temporal |
                  CoCoContextProjection.Durable
                : CoCoContextProjection.Temporal;
            CoCoIntentId[] intents = null;
            if (withEvent)
            {
                Ensure(builder.TryRegisterIntent(
                    ids.IntentId,
                    4,
                    new CoCoIntentReducerFactoryToken<
                        TemporalHostIntent,
                        TemporalHostIntentReducer,
                        TemporalHostIntentReducerFactory>(
                        TemporalHostDefaults.IntentReducerFingerprint),
                    out CoCoDiagnostic intent), intent);
                Ensure(builder.TryRegisterEventToIntentDeclaration<
                    TemporalHostEvent,
                    TemporalHostIntent>(
                    ids.EventDomainId,
                    ids.EventTypeId,
                    ids.IntentId,
                    out CoCoDiagnostic eventDeclaration), eventDeclaration);
                intents = new[] { ids.IntentId };
            }

            Ensure(builder.TryRegisterStateBlock(
                ids.GraphStateBlockId,
                CoCoStateBlockOwner.Graph,
                out CoCoDiagnostic graphBlock), graphBlock);
            Ensure(builder.TryRegisterStateSlot(
                ids.GraphStateBlockId,
                ids.GraphStateSlotId,
                projection,
                CoCoContextRestorePolicy.Stored,
                TemporalHostDefaults.GraphState(ids),
                TemporalHostDefaults.GraphStateFingerprint,
                default,
                null,
                out CoCoDiagnostic graphSlot), graphSlot);
            Ensure(builder.TryRegisterStateBlock(
                ids.ActorStateBlockId,
                CoCoStateBlockOwner.Actor,
                out CoCoDiagnostic actorBlock), actorBlock);
            Ensure(builder.TryRegisterStateSlot(
                ids.ActorStateBlockId,
                ids.ActorStateSlotId,
                projection,
                CoCoContextRestorePolicy.Stored,
                TemporalHostDefaults.ActorStateValue,
                TemporalHostDefaults.ActorStateFingerprint,
                actorCodec,
                null,
                out CoCoDiagnostic actorSlot), actorSlot);
            Ensure(builder.TryRegisterState(
                ids.StateDescriptorId,
                1U,
                new HostTestStateConfigFreezer(),
                new CoCoStateRuntimeRegistration<
                    TemporalHostLogic,
                    HostTestStateConfigSchema,
                    TemporalHostMemory>(HostTestSchemas.State, false),
                intents,
                null,
                new[] { ids.GraphStateBlockId, ids.ActorStateBlockId },
                out CoCoDiagnostic state), state);
            Ensure(builder.TryFreeze(
                out CoCoGraphDescriptorCatalog catalog,
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
        internal TemporalHostTestIds Ids { get; set; }
        internal TemporalHostBindingProvider Provider { get; set; }
        internal CoCoStateGraphAsset Asset { get; set; }
        internal GameObject GameObject { get; set; }
        internal CoCoStateGraphHost Host { get; set; }
        internal TemporalActorRestoreBinding Binding { get; set; }
    }

    internal static class TemporalHostTestHarness
    {
        internal static TemporalHostTestScenario Create(
            int historyCapacity,
            bool withEvent = false,
            bool assignRestoreBinding = true,
            bool withDurableProjection = false,
            ICoCoContextValueCodec<int> actorCodec = null)
        {
            TemporalHostTestIds ids = TemporalHostTestIds.Create();
            var provider = new TemporalHostBindingProvider(
                ids,
                withEvent,
                withDurableProjection,
                actorCodec);
            if (!CoCoStateGraphProjectBindings.TryInstall(
                    provider,
                    out CoCoDiagnostic install))
            {
                throw new InvalidOperationException(install.Message);
            }

            CoCoStateGraphAsset asset = ScriptableObject.CreateInstance<CoCoStateGraphAsset>();
            asset.EnsureAssetIdentity(Guid.NewGuid().ToString("N"));
            var state = new CoCoStateGraphStateRecord(
                Serialize(ids.StateId),
                default,
                "Temporal Host State",
                Serialize(ids.StateDescriptorId),
                new HostTestStateConfig { Value = 1 });
            var layer = new CoCoStateGraphLayerRecord(Serialize(ids.LayerId), "Base");
            layer.InitialStateId = Serialize(ids.StateId);
            layer.States.Add(state);
            asset.Layers.Add(layer);
            if (withEvent)
            {
                asset.EventAdapterDeclarations.Add(
                    new CoCoStateGraphEventAdapterDeclarationRecord(
                        Serialize(ids.EventTypeId),
                        Serialize(ids.IntentId)));
            }

            var gameObject = new GameObject("Pre6 Temporal Host Test");
            CoCoStateGraphHost host = gameObject.AddComponent<CoCoStateGraphHost>();
            var binding = gameObject.AddComponent<TemporalActorRestoreBinding>();
            binding.Configure(ids.ActorStateSlotId);
            SetField(host, "stateGraphAsset", asset);
            SetField(host, "driver", CoCoStateGraphDriver.Manual);
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
            CoCoStateGraphHost host = gameObject.AddComponent<CoCoStateGraphHost>();
            var binding = gameObject.AddComponent<TemporalActorRestoreBinding>();
            binding.Configure(source.Ids.ActorStateSlotId);
            SetField(host, "stateGraphAsset", source.Asset);
            SetField(host, "driver", CoCoStateGraphDriver.Manual);
            SetField(host, "autoStart", false);
            SetField(host, "contextFrameCapacity", 4);
            SetField(host, "eventLaneCapacity", 4);
            SetField(host, "eventOutboxCapacity", 4);
            SetField(host, "traceCapacity", 64);
            SetField(host, "temporalHistoryCapacity", historyCapacity);
            SetField(host, "actorContextBinding", binding);
            if (source.Asset.EventAdapterDeclarations.Count > 0)
            {
                var adapter = gameObject.AddComponent<TemporalHostEventAdapterComponent>();
                SetField(host, "eventAdapters", new MonoBehaviour[] { adapter });
            }

            SetField(host, "contextRestoreBinding", binding);
            return new TemporalHostTestScenario
            {
                Ids = source.Ids,
                Provider = source.Provider,
                Asset = source.Asset,
                GameObject = gameObject,
                Host = host,
                Binding = binding
            };
        }

        internal static CoCoStateGraphRuntime GetRuntime(CoCoStateGraphHost host)
        {
            FieldInfo field = typeof(CoCoStateGraphHost).GetField(
                "_runtime",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return (CoCoStateGraphRuntime)field?.GetValue(host);
        }

        internal static void SetRestoreBinding(
            CoCoStateGraphHost host,
            MonoBehaviour binding) =>
            SetField(host, "contextRestoreBinding", binding);

        internal static CoCoStateGraphHostRuntimeBindings GetBindings(
            CoCoStateGraphHost host)
        {
            FieldInfo field = typeof(CoCoStateGraphHost).GetField(
                "_bindings",
                BindingFlags.Instance | BindingFlags.NonPublic);
            return (CoCoStateGraphHostRuntimeBindings)field?.GetValue(host);
        }

        internal static int ReadActorValue(
            CoCoContextFrame frame,
            CoCoStateSlotId slotId)
        {
            if (!frame.Layout.TryResolveSlot(slotId, out CoCoStateSlot<int> slot))
            {
                throw new InvalidOperationException("Temporal Actor Slot could not be resolved.");
            }

            return frame.Read(slot);
        }

        internal static CoCoGraphStateRecord<int> ReadGraphState(
            CoCoContextFrame frame,
            CoCoStateSlotId slotId)
        {
            if (!frame.Layout.TryResolveSlot(
                    slotId,
                    out CoCoStateSlot<CoCoGraphStateRecord<int>> slot))
            {
                throw new InvalidOperationException("Temporal Graph Slot could not be resolved.");
            }

            return frame.Read(slot);
        }

        internal static CoCoEventPacket<TemporalHostEvent> Packet(
            TemporalHostTestScenario scenario,
            ulong eventSequence,
            int value,
            CoCoTimelineEpoch sourceEpoch)
        {
            if (!CoCoEventSequence.TryCreate(
                    eventSequence,
                    out CoCoEventSequence sequence) ||
                !CoCoActorEventEnvelope.TryCreate(
                    scenario.Ids.EventTypeId,
                    scenario.Ids.EventDomainId,
                    scenario.Host.GraphInstanceId,
                    scenario.Host.GraphInstanceId,
                    sourceEpoch,
                    new CoCoTimelineTick(1UL),
                    sequence,
                    CoCoEventDeliveryMode.Targeted,
                    CoCoEventReliability.Reliable,
                    default,
                    default,
                    default,
                    out CoCoActorEventEnvelope envelope) ||
                !CoCoEventPacket<TemporalHostEvent>.TryCreate(
                    envelope,
                    new TemporalHostEvent { Value = value },
                    out CoCoEventPacket<TemporalHostEvent> packet))
            {
                throw new InvalidOperationException("Temporal Host Event packet is invalid.");
            }

            return packet;
        }

        private static void SetField<TValue>(
            CoCoStateGraphHost host,
            string fieldName,
            TValue value)
        {
            FieldInfo field = typeof(CoCoStateGraphHost).GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new MissingFieldException(typeof(CoCoStateGraphHost).FullName, fieldName);
            }

            field.SetValue(host, value);
        }

        private static CoCoSerializedId128 Serialize(CoCoLayerId id) =>
            new CoCoSerializedId128(id.High, id.Low);

        private static CoCoSerializedId128 Serialize(CoCoStateId id) =>
            new CoCoSerializedId128(id.High, id.Low);

        private static CoCoSerializedId128 Serialize(CoCoStateDescriptorId id) =>
            new CoCoSerializedId128(id.High, id.Low);

        private static CoCoSerializedId128 Serialize(CoCoEventTypeId id) =>
            new CoCoSerializedId128(id.High, id.Low);

        private static CoCoSerializedId128 Serialize(CoCoIntentId id) =>
            new CoCoSerializedId128(id.High, id.Low);
    }
}
