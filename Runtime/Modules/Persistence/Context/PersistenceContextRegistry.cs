using System;
using System.Collections.Generic;
using CoCoFlow.Runtime.Modules.Persistence.Core;
using UnityEngine;

namespace CoCoFlow.Runtime.Modules.Persistence.Context
{
    public static class PersistenceContextRegistry
    {
        private static readonly Dictionary<string, PersistenceContext> Contexts =
            new Dictionary<string, PersistenceContext>();

        #region Public API

        public static void Register(PersistenceContext context)
        {
            if (context == null || string.IsNullOrEmpty(context.StableEntityId)) return;
            if (Contexts.TryGetValue(context.StableEntityId, out var previous) &&
                previous != null &&
                !ReferenceEquals(previous, context))
            {
                previous.CancelDeferredApply();
            }

            Contexts[context.StableEntityId] = context;

            var pendingSection = PersistenceSession.PendingDocument?.contextSection;
            if (pendingSection != null &&
                pendingSection.TryGetRecord(context.StableEntityId, out var record))
            {
                ApplyRecord(context, record, false);
            }
        }

        public static void Unregister(PersistenceContext context)
        {
            if (context == null || string.IsNullOrEmpty(context.StableEntityId)) return;
            if (Contexts.TryGetValue(context.StableEntityId, out var current) &&
                ReferenceEquals(current, context))
            {
                Contexts.Remove(context.StableEntityId);
            }
        }

        public static void Clear()
        {
            foreach (var context in Contexts.Values)
            {
                if (context != null)
                {
                    context.CancelDeferredApply();
                }
            }

            Contexts.Clear();
        }

        public static PersistenceContextSection CaptureSection()
        {
            var section = new PersistenceContextSection();
            foreach (var context in Contexts.Values)
            {
                if (context == null) continue;

                PersistenceContextOperationResult result = context.TryCaptureDetailed(
                    out PersistenceContextRecord record,
                    out string failure);
                switch (result)
                {
                    case PersistenceContextOperationResult.Applied:
                        section.AddOrReplace(record);
                        break;
                    case PersistenceContextOperationResult.Unsupported:
                        break;
                    case PersistenceContextOperationResult.Deferred:
                    case PersistenceContextOperationResult.Failed:
                        throw new InvalidOperationException(
                            FormatFailure(
                                "capture",
                                context.StableEntityId,
                                failure));
                }
            }

            return section;
        }

        public static void ApplySection(PersistenceContextSection section)
        {
            if (section == null) return;

            foreach (var record in section.records)
            {
                if (record == null || string.IsNullOrEmpty(record.stableEntityId)) continue;
                if (Contexts.TryGetValue(record.stableEntityId, out var context) && context != null)
                {
                    ApplyRecord(context, record, true);
                }
            }
        }

        #endregion

        #region Internal Logic

        private static void ApplyRecord(
            PersistenceContext context,
            PersistenceContextRecord record,
            bool throwOnFailure)
        {
            PersistenceContextOperationResult result = context.TryApplyDetailed(
                record,
                out string failure);
            if (result != PersistenceContextOperationResult.Deferred)
            {
                context.CancelDeferredApply();
            }

            if (result == PersistenceContextOperationResult.Applied ||
                result == PersistenceContextOperationResult.Unsupported)
            {
                return;
            }

            if (result == PersistenceContextOperationResult.Deferred)
            {
                if (context.TryScheduleDeferredApply(record, out failure))
                {
                    return;
                }
            }

            string message = FormatFailure(
                "apply",
                context.StableEntityId,
                failure);
            if (throwOnFailure)
            {
                throw new InvalidOperationException(message);
            }

            Debug.LogError($"[PersistenceContextRegistry] {message}", context);
        }

        private static string FormatFailure(
            string operation,
            string stableEntityId,
            string failure)
        {
            string detail = string.IsNullOrEmpty(failure)
                ? "The operation failed without a diagnostic."
                : failure;
            return
                $"Persistence Context {operation} failed for '{stableEntityId}': {detail}";
        }

        #endregion
    }
}
