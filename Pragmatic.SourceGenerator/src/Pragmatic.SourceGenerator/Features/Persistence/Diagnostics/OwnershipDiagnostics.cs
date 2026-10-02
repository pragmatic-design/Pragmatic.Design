using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Diagnostics;

/// <summary>
///     Diagnostic descriptors for Data Ownership features.
///     Range: PRAG1100-PRAG1109
/// </summary>
internal static class OwnershipDiagnostics
{
    // NOTE: PRAG1100 (OwnedEntityMustBePartial) was declared here but never reported — a dead copy.
    // The ID is alive in the companion analyzer (Pragmatic.SourceGenerator.Analyzers,
    // NotPartialDiagnosticDescriptors), which is its only source: it reports on the type declaration so
    // the "Make class partial" code fix can act. Do not re-add it here without removing the analyzer's copy.

    // NOTE: PRAG1102 (OwnedEntityRequiresEntity) was declared here but never reported — [HasOwner]
    // without [Entity] is simply ignored by the transform. Removed: do not reuse the ID for anything else.

    public static readonly DiagnosticDescriptor OwnedEntityManualOwnerId = DiagnosticFactory.Info(
        "PRAG1104",
        "OwnerId manually declared",
        "Type '{0}' already declares OwnerId — the source generator will skip OwnerId generation but will still generate the OwnershipFilter",
        "No action required. Remove the manual OwnerId property to use the auto-generated version.");
}
