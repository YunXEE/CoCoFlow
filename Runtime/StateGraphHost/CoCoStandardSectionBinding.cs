using System;
using System.Collections.Generic;

namespace CoCoFlow.Runtime.Core
{
    /// <summary>
    /// One Operation Section exported by a standard-path Operator registrar.
    /// </summary>
    public readonly struct CoCoStandardOperationRegistration
    {
        public CoCoStandardOperationRegistration(
            Type sectionType,
            CoCoOperationSectionId sectionId)
        {
            SectionType = sectionType ??
                          throw new ArgumentNullException(nameof(sectionType));
            SectionId = sectionId;
        }

        public Type SectionType { get; }
        public CoCoOperationSectionId SectionId { get; }
        public bool IsValid => SectionType != null &&
                               typeof(ICoCoOperationSection).IsAssignableFrom(SectionType) &&
                               SectionId.IsValid;
    }

    /// <summary>
    /// Complete standard-path registration owned by one Operator module.
    /// StandardBinding first registers its catalog declarations, then walks
    /// the compiled manifests in frozen order and binds only the requested
    /// Operations and Context slots.
    /// </summary>
    public interface ICoCoStandardOperatorRegistrar
    {
        IReadOnlyList<CoCoStandardOperationRegistration> Operations { get; }

        IReadOnlyList<CoCoStateBlockId> ContextBlocks { get; }

        bool RegisterCatalog(CoCoGraphDescriptorCatalogBuilder builder);

        bool TryBindOperation(
            CoCoOperationSectionId sectionId,
            CoCoStateGraphHostBindingBuilder bindingBuilder,
            out CoCoDiagnostic diagnostic);

        bool TryBindContextSlot(
            CoCoStateBlockId blockId,
            CoCoStateSlotId slotId,
            CoCoStateGraphHostBindingBuilder bindingBuilder,
            out CoCoDiagnostic diagnostic);
    }
}
