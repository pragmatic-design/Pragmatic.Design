using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Traits.Diagnostics;

/// <summary>
/// Diagnostic descriptors for the Trait feature.
/// The PRAG2600–PRAG2699 range is shared with Resource, which claims PRAG2602–PRAG2649; traits use
/// PRAG2600–PRAG2601 and PRAG2650 upwards, so neither side has to guess what the other took.
/// </summary>
internal static class TraitDiagnostics
{
    /// <summary>PRAG2600: Trait attribute requires [Entity] on the target class.</summary>
    public static readonly DiagnosticDescriptor TraitRequiresEntity = DiagnosticFactory.Error(
        "PRAG2600",
        "Trait requires [Entity]",
        "Type '{0}' has [{1}] but is not annotated with [Entity].",
        "Add [Entity] to the class before applying traits.");

    /// <summary>PRAG2601: Trait endpoint generation requires [Resource] for route resolution.</summary>
    public static readonly DiagnosticDescriptor TraitRequiresResource = DiagnosticFactory.Warning(
        "PRAG2601",
        "Trait endpoint requires [Resource]",
        "Type '{0}' has [{1}] but no [Resource] — trait endpoints will not be generated.",
        "Add [Resource(\"segment\")] to enable trait endpoint generation.");

    /// <summary>
    ///     PRAG2606: a trait whose generated actions inject <c>IClock</c>, in a compilation with no
    ///     Pragmatic.Temporal to supply one.
    /// </summary>
    /// <remarks>
    ///     The generated action stamps its rows through <c>IClock</c>, and the only registration of
    ///     that service lives in Pragmatic.Temporal (and Pragmatic.Jobs, which registers the same
    ///     instance). Without either, the field is left null and the first upload fails at runtime
    ///     with nothing at compile time pointing at the cause — a consumer lost time to exactly this.
    /// </remarks>
    public static readonly DiagnosticDescriptor TraitRequiresClock = DiagnosticFactory.Warning(
        "PRAG2606",
        "Trait actions need a clock",
        "Type '{0}' has [{1}], whose generated actions require IClock — no Pragmatic.Temporal in this compilation.",
        "Reference Pragmatic.Temporal and call AddPragmaticTemporal(), which registers IClock.");

    // PRAG2607 was TraitListIgnoresParentVisibility — removed once the generator closed the gap it
    // warned about. The trait child now carries a ParentVisibilityFilter, so the condition cannot
    // arise. Do not reuse the ID: it appeared in one published build.

    /// <summary>
    ///     PRAG2651: a thumbnail was asked for in a compilation with no Pragmatic.Imaging to derive it.
    /// </summary>
    /// <remarks>
    ///     The derivation is emitted only when the library that performs it is referenced — the
    ///     decision is the generator's, at compile time, rather than a nullable capability probed at
    ///     run time. That leaves one bad outcome to guard: an attribute that reads as configured and
    ///     produces nothing. Every upload would keep storing the original whole, and no line anywhere
    ///     would say why.
    /// </remarks>
    public static readonly DiagnosticDescriptor TraitThumbnailRequiresImaging = DiagnosticFactory.Warning(
        "PRAG2651",
        "Thumbnails need Pragmatic.Imaging",
        "Type '{0}' asks [HasAttachments] for a {1}x{2} thumbnail — no Pragmatic.Imaging in this compilation, so none is generated.",
        "Reference Pragmatic.Imaging from this module. ⚠️ It P/Invokes a native library shipped per "
        + "runtime identifier, so the RID being deployed has to be one the package carries.");

    /// <summary>PRAG2650: the trait's navigation property name is already taken on the parent.</summary>
    public static readonly DiagnosticDescriptor TraitNavigationCollides = DiagnosticFactory.Error(
        "PRAG2650",
        "Trait navigation name already used",
        "Type '{0}' already declares a member named '{1}', which [{2}] also generates.",
        "Rename the existing member: the generated navigation property cannot be renamed. " +
        "Without this the build fails with CS0102 inside generated code, which says nothing about the trait.");
}
