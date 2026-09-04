#if UNITY_EDITOR
using System;
using System.Globalization;
using CoCoFlow.Runtime.Core;
using CoCoFlow.Runtime.Modules.Persistence.Context;
using CoCoFlow.Runtime.Modules.Persistence.Core;

namespace CoCoFlow.Runtime.Modules.Persistence
{
    /// <summary>Metadata copied from one successfully decoded on-disk save record.</summary>
    internal readonly struct CoCoPersistedFrameDebugInfo
    {
        internal CoCoPersistedFrameDebugInfo(
            CoCoTemporalFrameInfo sourceFrame,
            int slotIndex,
            DateTimeOffset updatedUtc)
        {
            SourceFrame = sourceFrame;
            SlotIndex = slotIndex;
            UpdatedUtc = updatedUtc;
        }

        internal CoCoTemporalFrameInfo SourceFrame { get; }
        internal int SlotIndex { get; }
        internal DateTimeOffset UpdatedUtc { get; }
        internal bool IsValid =>
            SourceFrame.IsValid && SlotIndex >= 0 && UpdatedUtc != default;
    }

    /// <summary>
    /// Editor-only read seam for the newest valid StateGraph frame already
    /// written to a standard save slot. It never captures, imports, or mutates
    /// a persistence session.
    /// </summary>
    internal static class CoCoStateGraphPersistenceDebugReader
    {
        internal static bool TryReadLatestPersistedFrame(
            CoCoStateGraphHost host,
            out CoCoPersistedFrameDebugInfo info,
            out string failure)
        {
            info = default;
            failure = string.Empty;
            if (host == null)
            {
                return false;
            }

            PersistenceContext context = host.GetComponent<PersistenceContext>();
            if (context == null || string.IsNullOrEmpty(context.StableEntityId))
            {
                return false;
            }

            bool found = false;
            DateTimeOffset latestUtc = default;
            int latestSlot = -1;
            CoCoTemporalFrameInfo latestFrame = default;
            string firstFailure = string.Empty;
            int slotCount = Math.Max(0, PersistenceSaveLoadSystem.MaxSaveSlots);
            for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
            {
                try
                {
                    if (!PersistenceFileStore.TryReadExistingDocument(
                            slotIndex,
                            out PersistenceSaveDocument document) ||
                        document?.contextSection == null ||
                        !document.contextSection.TryGetRecord(
                            context.StableEntityId,
                            out PersistenceContextRecord record) ||
                        record == null ||
                        !record.IsStateGraphContextRecord)
                    {
                        continue;
                    }

                    if (!record.TryGetStateGraphContextPayload(out byte[] payload))
                    {
                        if (string.IsNullOrEmpty(firstFailure))
                        {
                            firstFailure =
                                $"Save slot {slotIndex} contains an empty StateGraph payload.";
                        }

                        continue;
                    }

                    string updatedText = document.metadata?.updatedUtc;
                    if (!DateTimeOffset.TryParse(
                            updatedText,
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.RoundtripKind,
                            out DateTimeOffset updatedUtc))
                    {
                        if (string.IsNullOrEmpty(firstFailure))
                        {
                            firstFailure =
                                $"Save slot {slotIndex} has no valid updatedUtc metadata.";
                        }

                        continue;
                    }

                    if (!host.TryDecodePersistenceDebugFrame(
                            payload,
                            out CoCoTemporalFrameInfo frame,
                            out CoCoDiagnostic diagnostic))
                    {
                        if (string.IsNullOrEmpty(firstFailure))
                        {
                            firstFailure = diagnostic.Message;
                        }

                        continue;
                    }

                    if (!found ||
                        updatedUtc > latestUtc ||
                        (updatedUtc == latestUtc && slotIndex < latestSlot))
                    {
                        found = true;
                        latestUtc = updatedUtc;
                        latestSlot = slotIndex;
                        latestFrame = frame;
                    }
                }
                catch (Exception exception)
                {
                    if (string.IsNullOrEmpty(firstFailure))
                    {
                        firstFailure =
                            $"Save slot {slotIndex} could not be read: {exception.Message}";
                    }
                }
            }

            if (!found)
            {
                failure = firstFailure;
                return false;
            }

            info = new CoCoPersistedFrameDebugInfo(
                latestFrame,
                latestSlot,
                latestUtc);
            return info.IsValid;
        }
    }
}
#endif
