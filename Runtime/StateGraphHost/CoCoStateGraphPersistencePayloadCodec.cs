using System;

namespace CoCoFlow.Runtime.Core
{
    internal readonly struct CoCoStateGraphPersistenceEnvelope
    {
        private readonly byte[] _payload;

        internal CoCoStateGraphPersistenceEnvelope(
            byte[] payload,
            int durableOffset,
            int durableLength,
            double deltaTime,
            CoCoTimelinePosition timelinePosition)
        {
            _payload = payload;
            DurableOffset = durableOffset;
            DurableLength = durableLength;
            DeltaTime = deltaTime;
            TimelinePosition = timelinePosition;
        }

        internal int DurableOffset { get; }
        internal int DurableLength { get; }
        internal double DeltaTime { get; }
        internal CoCoTimelinePosition TimelinePosition { get; }
        internal bool IsValid =>
            _payload != null &&
            DurableOffset >= 0 &&
            DurableLength > 0 &&
            DurableOffset <= _payload.Length - DurableLength &&
            DeltaTime > 0d &&
            !double.IsNaN(DeltaTime) &&
            !double.IsInfinity(DeltaTime) &&
            TimelinePosition.IsValid;

        internal ReadOnlySpan<byte> DurablePayload =>
            IsValid
                ? new ReadOnlySpan<byte>(_payload, DurableOffset, DurableLength)
                : ReadOnlySpan<byte>.Empty;
    }

    internal sealed class CoCoStateGraphPersistencePayloadCodec
    {
        private const uint Magic = 0x43534750U;
        private const uint EnvelopeVersion = 1U;
        private const int EnvelopeHeaderSize = 44;

        private readonly CoCoGraphId _graphId;
        private readonly CoCoContextProjectionCodec _projection;

        private CoCoStateGraphPersistencePayloadCodec(
            CoCoGraphId graphId,
            CoCoContextProjectionCodec projection)
        {
            _graphId = graphId;
            _projection = projection;
        }

        internal CoCoContextProjectionCodec Projection => _projection;

        internal static bool TryCreate(
            CoCoGraphId graphId,
            CoCoContextFrameLayout layout,
            CoCoContextCodecRegistry codecs,
            out CoCoStateGraphPersistencePayloadCodec codec,
            out CoCoDiagnostic diagnostic)
        {
            codec = null;
            CoCoDiagnosticCode diagnosticCode = CoCoDiagnosticCode.None;
            CoCoContextProjectionCodec projection = null;
            if (!graphId.IsValid ||
                !CoCoContextProjectionCodec.TryCreate(
                    layout,
                    codecs,
                    CoCoContextProjection.Durable,
                    out projection,
                    out diagnosticCode) ||
                projection.MaxEncodedSize > int.MaxValue - EnvelopeHeaderSize)
            {
                diagnostic = RestoreError(
                    diagnosticCode == CoCoDiagnosticCode.None
                        ? CoCoDiagnosticCode.InvalidFrameLayout
                        : diagnosticCode,
                    "StateGraph Persistence requires one valid Graph and Durable Context projection.");
                return false;
            }

            codec = new CoCoStateGraphPersistencePayloadCodec(graphId, projection);
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryEncode(
            CoCoContextFrame frame,
            out byte[] payload,
            out CoCoDiagnostic diagnostic)
        {
            payload = null;
            if (!frame.IsAlive ||
                frame.Header.TickFrame.DeltaTime <= 0d ||
                double.IsNaN(frame.Header.TickFrame.DeltaTime) ||
                double.IsInfinity(frame.Header.TickFrame.DeltaTime) ||
                !frame.Header.TickFrame.TimelinePosition.IsValid)
            {
                diagnostic = RestoreError(
                    CoCoDiagnosticCode.InvalidFrameHandle,
                    "StateGraph Persistence capture requires one live committed ContextFrame.");
                return false;
            }

            byte[] encoded = new byte[EnvelopeHeaderSize + _projection.MaxEncodedSize];
            if (!_projection.TryEncode(
                    frame,
                    new Span<byte>(
                        encoded,
                        EnvelopeHeaderSize,
                        _projection.MaxEncodedSize),
                    out int durableLength,
                    out CoCoDiagnosticCode diagnosticCode))
            {
                diagnostic = RestoreError(
                    diagnosticCode,
                    "Durable Context projection rejected the committed ContextFrame.");
                return false;
            }

            int cursor = 0;
            CoCoStateFlowBinary.WriteUInt32(encoded, ref cursor, Magic);
            CoCoStateFlowBinary.WriteUInt32(encoded, ref cursor, EnvelopeVersion);
            CoCoStateFlowBinary.WriteUInt64(encoded, ref cursor, _graphId.High);
            CoCoStateFlowBinary.WriteUInt64(encoded, ref cursor, _graphId.Low);
            CoCoStateFlowBinary.WriteUInt64(
                encoded,
                ref cursor,
                unchecked((ulong)BitConverter.DoubleToInt64Bits(
                    frame.Header.TickFrame.DeltaTime)));
            CoCoStateFlowBinary.WriteUInt64(
                encoded,
                ref cursor,
                unchecked((ulong)BitConverter.DoubleToInt64Bits(
                    frame.Header.TickFrame.TimelinePosition.Seconds)));
            CoCoStateFlowBinary.WriteUInt32(encoded, ref cursor, (uint)durableLength);
            if (cursor != EnvelopeHeaderSize ||
                durableLength <= 0 ||
                durableLength > _projection.MaxEncodedSize)
            {
                diagnostic = RestoreError(
                    CoCoDiagnosticCode.InvalidRestoreMetadata,
                    "StateGraph Persistence envelope length is invalid.");
                return false;
            }

            int exactLength = EnvelopeHeaderSize + durableLength;
            if (encoded.Length != exactLength)
            {
                Array.Resize(ref encoded, exactLength);
            }

            payload = encoded;
            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal bool TryDecode(
            byte[] payload,
            out CoCoStateGraphPersistenceEnvelope envelope,
            out CoCoProjectionRestoreSource persistedSource,
            out CoCoDiagnostic diagnostic)
        {
            envelope = default;
            persistedSource = default;
            int cursor = 0;
            ReadOnlySpan<byte> source = payload;
            if (payload == null ||
                source.Length < EnvelopeHeaderSize ||
                !CoCoStateFlowBinary.TryReadUInt32(source, ref cursor, out uint magic) ||
                !CoCoStateFlowBinary.TryReadUInt32(source, ref cursor, out uint version) ||
                !CoCoStateFlowBinary.TryReadUInt64(source, ref cursor, out ulong graphHigh) ||
                !CoCoStateFlowBinary.TryReadUInt64(source, ref cursor, out ulong graphLow) ||
                !CoCoStateFlowBinary.TryReadUInt64(source, ref cursor, out ulong deltaBits) ||
                !CoCoStateFlowBinary.TryReadUInt64(source, ref cursor, out ulong positionBits) ||
                !CoCoStateFlowBinary.TryReadUInt32(source, ref cursor, out uint durableLengthValue))
            {
                diagnostic = RestoreError(
                    CoCoDiagnosticCode.InvalidRestoreMetadata,
                    "StateGraph Persistence envelope is truncated.");
                return false;
            }

            double deltaTime = BitConverter.Int64BitsToDouble(unchecked((long)deltaBits));
            double positionSeconds =
                BitConverter.Int64BitsToDouble(unchecked((long)positionBits));
            if (magic != Magic ||
                version != EnvelopeVersion ||
                graphHigh != _graphId.High ||
                graphLow != _graphId.Low ||
                deltaTime <= 0d ||
                double.IsNaN(deltaTime) ||
                double.IsInfinity(deltaTime) ||
                !CoCoTimelinePosition.TryCreate(
                    positionSeconds,
                    out CoCoTimelinePosition timelinePosition) ||
                durableLengthValue > int.MaxValue)
            {
                diagnostic = RestoreError(
                    graphHigh != _graphId.High || graphLow != _graphId.Low
                        ? CoCoDiagnosticCode.InvalidIdentifier
                        : CoCoDiagnosticCode.InvalidRestoreMetadata,
                    "StateGraph Persistence envelope does not match the current Graph or time contract.");
                return false;
            }

            int durableLength = (int)durableLengthValue;
            CoCoDiagnosticCode diagnosticCode = CoCoDiagnosticCode.None;
            if (cursor != EnvelopeHeaderSize ||
                durableLength <= 0 ||
                durableLength != source.Length - cursor ||
                durableLength > _projection.MaxEncodedSize ||
                !_projection.TryValidateSource(
                    source.Slice(cursor, durableLength),
                    out persistedSource,
                    out int bytesRead,
                    out diagnosticCode) ||
                bytesRead != durableLength)
            {
                diagnostic = RestoreError(
                    diagnosticCode == CoCoDiagnosticCode.None
                        ? CoCoDiagnosticCode.InvalidRestoreMetadata
                        : diagnosticCode,
                    "StateGraph Persistence Durable payload is malformed or incompatible.");
                return false;
            }

            envelope = new CoCoStateGraphPersistenceEnvelope(
                payload,
                cursor,
                durableLength,
                deltaTime,
                timelinePosition);
            if (!envelope.IsValid)
            {
                envelope = default;
                persistedSource = default;
                diagnostic = RestoreError(
                    CoCoDiagnosticCode.InvalidRestoreMetadata,
                    "StateGraph Persistence envelope did not produce a valid Durable slice.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        internal static bool TryCreateImportedTickFrame(
            CoCoActorClock targetClock,
            in CoCoStateGraphPersistenceEnvelope envelope,
            in CoCoProjectionRestoreSource persistedSource,
            out CoCoTickFrame imported,
            out CoCoDiagnostic diagnostic)
        {
            imported = default;
            diagnostic = CoCoDiagnostic.None;
            if (targetClock == null || !envelope.IsValid || !persistedSource.IsValid)
            {
                diagnostic = RestoreError(
                    CoCoDiagnosticCode.InvalidRestoreMetadata,
                    "StateGraph Persistence import metadata is incomplete.");
                return false;
            }

            ulong maximumEpoch = targetClock.TimelineEpoch.Value >
                                 persistedSource.TimelineEpoch.Value
                ? targetClock.TimelineEpoch.Value
                : persistedSource.TimelineEpoch.Value;
            ulong maximumSequence = targetClock.ExecutionSequence.Value >
                                    persistedSource.ExecutionSequence.Value
                ? targetClock.ExecutionSequence.Value
                : persistedSource.ExecutionSequence.Value;
            if (maximumEpoch == ulong.MaxValue ||
                maximumSequence >= ulong.MaxValue - 1UL ||
                persistedSource.Tick.Value == ulong.MaxValue ||
                !CoCoTickFrame.TryCreate(
                    envelope.DeltaTime,
                    targetClock.TimelineId,
                    envelope.TimelinePosition,
                    persistedSource.Tick,
                    targetClock.ClockDomainId,
                    new CoCoExecutionSequence(maximumSequence + 1UL),
                    new CoCoTimelineEpoch(maximumEpoch + 1UL),
                    out imported,
                    out diagnostic))
            {
                if (!diagnostic.IsError)
                {
                    diagnostic = RestoreError(
                        CoCoDiagnosticCode.InvalidRestoreMetadata,
                        "StateGraph Persistence cannot allocate a strictly newer authority with room for the next Tick.");
                }

                return false;
            }

            return true;
        }

        internal static bool TryCreatePersistedSourceInfo(
            in CoCoStateGraphPersistenceEnvelope envelope,
            in CoCoProjectionRestoreSource persistedSource,
            out CoCoTemporalFrameInfo sourceInfo,
            out CoCoDiagnostic diagnostic)
        {
            sourceInfo = default;
            diagnostic = CoCoDiagnostic.None;
            if (!envelope.IsValid ||
                !persistedSource.IsValid ||
                !CoCoTickFrame.TryCreate(
                    envelope.DeltaTime,
                    persistedSource.TimelineId,
                    envelope.TimelinePosition,
                    persistedSource.Tick,
                    persistedSource.ClockDomainId,
                    persistedSource.ExecutionSequence,
                    persistedSource.TimelineEpoch,
                    out CoCoTickFrame sourceTick,
                    out diagnostic))
            {
                if (!diagnostic.IsError)
                {
                    diagnostic = RestoreError(
                        CoCoDiagnosticCode.InvalidRestoreMetadata,
                        "StateGraph Persistence source metadata is invalid.");
                }

                return false;
            }

            sourceInfo = new CoCoTemporalFrameInfo(
                persistedSource.GraphInstanceId,
                sourceTick,
                persistedSource.Revision,
                CoCoContextFrameOrigin.Commit());
            if (!sourceInfo.IsValid)
            {
                sourceInfo = default;
                diagnostic = RestoreError(
                    CoCoDiagnosticCode.InvalidRestoreMetadata,
                    "StateGraph Persistence source authority is invalid.");
                return false;
            }

            diagnostic = CoCoDiagnostic.None;
            return true;
        }

        private static CoCoDiagnostic RestoreError(
            CoCoDiagnosticCode code,
            string message) =>
            CoCoDiagnostic.Error(
                CoCoDiagnosticDomain.Restore,
                code == CoCoDiagnosticCode.None
                    ? CoCoDiagnosticCode.InvalidRestoreMetadata
                    : code,
                message);
    }
}
