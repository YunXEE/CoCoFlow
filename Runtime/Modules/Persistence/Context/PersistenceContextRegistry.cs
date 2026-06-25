using System.Collections.Generic;

namespace CoCoFlow.Runtime.Modules.Persistence
{
    public static class PersistenceContextRegistry
    {
        private static readonly Dictionary<string, PersistenceContext> Contexts =
            new Dictionary<string, PersistenceContext>();

        #region Public API

        public static void Register(PersistenceContext context)
        {
            if (context == null || string.IsNullOrEmpty(context.StableEntityId)) return;
            Contexts[context.StableEntityId] = context;

            var pendingSection = PersistenceSession.PendingDocument?.contextSection;
            if (pendingSection != null &&
                pendingSection.TryGetRecord(context.StableEntityId, out var record))
            {
                context.TryApply(record);
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
            Contexts.Clear();
        }

        public static PersistenceContextSection CaptureSection()
        {
            var section = new PersistenceContextSection();
            foreach (var context in Contexts.Values)
            {
                if (context != null && context.TryCapture(out var record))
                {
                    section.AddOrReplace(record);
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
                    context.TryApply(record);
                }
            }
        }

        #endregion
    }
}
