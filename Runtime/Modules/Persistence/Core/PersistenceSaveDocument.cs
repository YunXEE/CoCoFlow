using System;

namespace CoCoFlow.Runtime.Modules.Persistence
{
    [Serializable]
    public sealed class PersistenceSaveDocument
    {
        public const int CurrentSchemaVersion = 1;

        public int schemaVersion = CurrentSchemaVersion;
        public PersistenceSaveSlotMetadata metadata = new PersistenceSaveSlotMetadata();
        public PersistenceContextSection contextSection = new PersistenceContextSection();
        public PersistenceContainerSection containerSection = new PersistenceContainerSection();

        public static PersistenceSaveDocument Create(
            int slotIndex,
            PersistenceContextSection contextSection,
            PersistenceContainerSection containerSection)
        {
            var document = new PersistenceSaveDocument
            {
                schemaVersion = CurrentSchemaVersion,
                metadata = PersistenceSaveSlotMetadata.Create(slotIndex),
                contextSection = contextSection ?? new PersistenceContextSection(),
                containerSection = containerSection ?? new PersistenceContainerSection()
            };
            document.metadata.Touch();
            return document;
        }

        public static PersistenceSaveDocument MigrateToCurrentSchema(PersistenceSaveDocument document)
        {
            if (document == null) return null;
            if (document.schemaVersion > CurrentSchemaVersion)
            {
                throw new NotSupportedException(
                    $"Save schema {document.schemaVersion} is newer than supported schema {CurrentSchemaVersion}.");
            }

            if (document.metadata == null)
            {
                document.metadata = new PersistenceSaveSlotMetadata();
            }

            if (document.contextSection == null)
            {
                document.contextSection = new PersistenceContextSection();
            }

            if (document.containerSection == null)
            {
                document.containerSection = new PersistenceContainerSection();
            }

            document.schemaVersion = CurrentSchemaVersion;
            return document;
        }
    }
}
