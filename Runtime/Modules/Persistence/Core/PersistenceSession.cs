using CoCoFlow.Runtime.Modules.Persistence.Container;
using CoCoFlow.Runtime.Modules.Persistence.Context;

namespace CoCoFlow.Runtime.Modules.Persistence.Core
{
    public static class PersistenceSession
    {
        private static PersistenceSaveDocument _pendingDocument;

        public static PersistenceSaveDocument PendingDocument => _pendingDocument;
        public static bool HasPendingDocument => _pendingDocument != null;

        #region Public API

        public static PersistenceSaveDocument Capture(int slotIndex)
        {
            var contextSection = PersistenceContextRegistry.CaptureSection();
            var containerSection = PersistenceContainerStore.CaptureActiveSection();
            return PersistenceSaveDocument.Create(slotIndex, contextSection, containerSection);
        }

        public static void SetPendingDocument(PersistenceSaveDocument document)
        {
            _pendingDocument = document;
        }

        public static void ApplyPendingDocument()
        {
            if (_pendingDocument == null) return;

            PersistenceContainerStore.ApplyActiveSection(_pendingDocument.containerSection);
            PersistenceContextRegistry.ApplySection(_pendingDocument.contextSection);
        }

        public static void ClearPendingDocument()
        {
            _pendingDocument = null;
        }

        #endregion
    }
}
