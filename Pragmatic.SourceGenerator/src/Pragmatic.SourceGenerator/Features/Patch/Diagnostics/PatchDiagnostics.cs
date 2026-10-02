using Microsoft.CodeAnalysis;

namespace Pragmatic.SourceGenerator.Features.Patch.Diagnostics;

/// <summary>
///     Diagnostic descriptors for the Patch feature.
///     Range: PRAG2200 - PRAG2249. (PRAG1900-1999 belongs to Documents — see docs/diagnostics.md.)
/// </summary>
internal static class PatchDiagnostics
{
    private const string Category = "Pragmatic.Patch";

    // PRAG2200 ([GeneratePatch] type must be partial) is the companion analyzer's, which reports it on the
    // declaration (NotPartialDiagnosticDescriptors); the generator skips the type silently.

    /// <summary>PRAG2201: Entity type could not be resolved from the attribute.</summary>
    public static readonly DiagnosticDescriptor EntityTypeNotFound = new(
        id: "PRAG2201",
        title: "Entity type not found",
        messageFormat: "Could not resolve entity type from [GeneratePatch] on '{0}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>PRAG2202: Entity has no settable properties suitable for patching.</summary>
    public static readonly DiagnosticDescriptor NoProperties = new(
        id: "PRAG2202",
        title: "No patchable properties",
        messageFormat: "Entity '{0}' has no settable properties suitable for patch generation on '{1}'",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>PRAG2203: a child collection on a patch has nothing to match its elements by.</summary>
    /// <remarks>
    ///     An error rather than a warning: the alternative is to leave the collection out of
    ///     <c>ApplyPatch</c>, which is a child that silently never gets written — the shape this
    ///     whole feature exists to remove.
    /// </remarks>
    public static readonly DiagnosticDescriptor CollectionElementsCannotBeMatched = new(
        id: "PRAG2203",
        title: "Patch collection elements cannot be matched",
        messageFormat: "'{0}.{1}' cannot be patched: its elements have no key in common with '{2}'. "
            + "Give the element DTO an Id, or the child entity a [LogicKey] the DTO also carries",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Error,
        isEnabledByDefault: true);

    /// <summary>PRAG2204: a child on a patch is a DTO that can neither create nor update its entity.</summary>
    public static readonly DiagnosticDescriptor RelatedDtoCannotWrite = new(
        id: "PRAG2204",
        title: "Patch child DTO cannot write its entity",
        messageFormat: "'{0}.{1}' is not written by ApplyPatch: '{2}' has neither [MapTo<T>] nor "
            + "[Patch<T>], so there is no way to create or update the entity behind it",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    /// <summary>PRAG2205: a patch-only element can update matched children but cannot add new ones.</summary>
    /// <remarks>
    ///     Info, not a warning: update-only is a legitimate contract, and the developer has already
    ///     chosen it by making the element a patch. It is said out loud because "the item I sent was
    ///     ignored" is otherwise indistinguishable from a bug.
    /// </remarks>
    public static readonly DiagnosticDescriptor PatchOnlyElementCannotBeAdded = new(
        id: "PRAG2205",
        title: "Patch collection updates matched elements only",
        messageFormat: "'{0}.{1}' updates children that already exist: '{2}' has no [MapTo<T>], so an "
            + "element matching nothing is skipped rather than created",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Info,
        isEnabledByDefault: true);

    /// <summary>PRAG2206: a [PatchIgnore] name that matches no property on the entity.</summary>
    /// <remarks>
    ///     Reported rather than ignored, because ignoring it does the opposite of what the line says:
    ///     the property the author wrote the attribute to protect stays in the patch, the contract
    ///     goes on offering it, and the endpoint goes on refusing it.
    /// </remarks>
    public static readonly DiagnosticDescriptor PatchIgnoreNameNotFound = new(
        id: "PRAG2206",
        title: "[PatchIgnore] names a property that does not exist",
        messageFormat: "[PatchIgnore] on '{0}' names '{1}', which is not a property of '{2}' — the "
            + "property it was meant to exclude is still in the patch",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);
}
