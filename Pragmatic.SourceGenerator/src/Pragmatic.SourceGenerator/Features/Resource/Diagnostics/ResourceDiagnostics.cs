using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Resource.Diagnostics;

/// <summary>
/// Diagnostic descriptors for the [Resource] feature.
/// Range: PRAG2602–PRAG2649 (PRAG2600–2601 are in TraitDiagnostics).
/// </summary>
internal static class ResourceDiagnostics
{
    /// <summary>PRAG2602: [Resource] segment must be lowercase kebab-case.</summary>
    public static readonly DiagnosticDescriptor SegmentNotKebabCase = DiagnosticFactory.Error(
        "PRAG2602",
        "Resource segment must be kebab-case",
        "[Resource] segment '{0}' on type '{1}' must be lowercase kebab-case (e.g. \"reservations\", \"room-types\").",
        "Use a lowercase kebab-case segment: letters, digits, and hyphens only.");

    /// <summary>PRAG2603: Duplicate [Resource] segment within the same boundary.</summary>
    public static readonly DiagnosticDescriptor DuplicateSegment = DiagnosticFactory.Error(
        "PRAG2603",
        "Duplicate resource segment",
        "Segment '{0}' is used by both '{1}' and '{2}' in boundary '{3}'.",
        "Each entity in a boundary must have a unique resource segment.");

    // NOTE: PRAG2604 (ShouldBePartial) was declared here but never reported — ResourceFeature never
    // checks the partial modifier. Removed: do not reuse the ID for anything else.

    /// <summary>PRAG2612: [Resource] with no capabilities scaffolds nothing.</summary>
    /// <remarks>
    ///     <c>Capabilities</c> defaults to <c>None</c>, and the feature skips a resource that has none
    ///     — no DTO, no action, no endpoint, and no word about it. The attribute reads as though it
    ///     published something; a consumer added it, found no route, and had nothing to go on. Naming
    ///     the omission is the whole fix: the default is deliberate, opting in is not a mistake to
    ///     prevent, only one to notice.
    /// </remarks>
    public static readonly DiagnosticDescriptor NoCapabilities = DiagnosticFactory.Warning(
        "PRAG2612",
        "Resource scaffolds nothing",
        "'{0}' is a [Resource(\"{1}\")] with no Capabilities, so nothing is scaffolded for it — no "
        + "DTO, no action, no endpoint. Set Capabilities (ResourceCapabilities.All, or the operations "
        + "wanted), or remove the attribute.",
        "Capabilities defaults to None. A resource that declares none is skipped in silence.");

    /// <summary>PRAG2607: a partial declaration decorates an operation that is not scaffolded.</summary>
    /// <remarks>
    ///     <para>
    ///         Customising a scaffolded operation means declaring its partial part and putting the
    ///         attributes on it — attributes on a partial class combine across its parts, so the body
    ///         keeps coming from the generator and the decoration from you. Matching is by type
    ///         identity, namespace and name.
    ///     </para>
    ///     <para>
    ///         Which is exactly why a mistyped name has to be reported. Nothing collides, nothing fails
    ///         to compile: you get an empty class of your own, the scaffolded operation keeps the
    ///         default you meant to replace, and the only way to notice is a permission check behaving
    ///         differently from what your file says.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor OverrideMatchesNoOperation = DiagnosticFactory.Warning(
        "PRAG2607",
        "This declaration decorates no scaffolded operation",
        "'{0}' carries operation attributes but matches no type [Resource] generates in this namespace. "
        + "Expected one of: {1}. The attributes have no effect and the scaffolded default still applies.",
        "A partial part is matched to a scaffolded operation by namespace and type name.");

    /// <summary>PRAG2608: the DTO a [ReturnsDto&lt;T&gt;] names has no projection.</summary>
    /// <remarks>
    ///     An error rather than a warning, because the failure it prevents is silent. A generated read
    ///     query projects in the database — <c>Select(TDto.Projection)</c> — and the executor falls back
    ///     to <c>OfType&lt;TResult&gt;()</c> when the DTO has none. That matches nothing, so the endpoint
    ///     answers 200 with an empty body, or 404 on a row that exists. Nothing throws, nothing logs.
    /// </remarks>
    public static readonly DiagnosticDescriptor DeclaredDtoHasNoProjection = DiagnosticFactory.Error(
        "PRAG2608",
        "This DTO cannot be projected",
        "'{0}' is declared as the shape of a scaffolded operation but has no projection, so the query "
        + "would return nothing. Add [MapFrom<{1}>] and [GenerateProjection] to it.",
        "A generated read query projects in the database and needs the DTO's Projection expression.");

    /// <summary>PRAG2609: the DTO maps from a different entity than the resource it is declared on.</summary>
    public static readonly DiagnosticDescriptor DeclaredDtoMapsFromAnotherEntity = DiagnosticFactory.Error(
        "PRAG2609",
        "This DTO maps from another entity",
        "'{0}' maps from '{1}', but the operation it is declared on reads '{2}'. Its projection cannot "
        + "be applied to this resource's query.",
        "The DTO's [MapFrom<T>] must name the entity the resource is declared on.");

    /// <summary>PRAG2605: [Resource] without Read capability — consider adding.</summary>
    public static readonly DiagnosticDescriptor ReadRecommended = DiagnosticFactory.Info(
        "PRAG2605",
        "Consider adding Read capability",
        "Type '{0}' has [Resource] with capabilities but Read is not included. GET /{1}/{{id}} will not be generated.",
        "Add ResourceCapabilities.Read if consumers need to fetch individual resources.");

    /// <summary>PRAG2610: a capability was asked for that cannot apply to this entity.</summary>
    /// <remarks>
    ///     Restore only means something where a delete leaves a row to restore, so the generator skips it
    ///     on an entity that is not <c>[SoftDelete]</c>. Skipping quietly is the failure this reports:
    ///     the author asked for an endpoint, got no endpoint, and nothing said why — the same silence
    ///     that let a curated collection come back unchanged from an operation answering 200.
    /// </remarks>
    public static readonly DiagnosticDescriptor CapabilityCannotApply = DiagnosticFactory.Warning(
        "PRAG2610",
        "This capability cannot apply to this entity",
        "'{0}' asks for ResourceCapabilities.{1}, but {2}. Nothing is generated for it.",
        "Remove the capability, or give the entity what it needs — [SoftDelete] is what makes a "
        + "delete leave a row that Restore can bring back.");

    /// <summary>PRAG2611: the entity is a resource and part of another aggregate at the same time.</summary>
    /// <remarks>
    ///     <para>
    ///         <c>[PartOf&lt;TParent&gt;]</c> says the entity has no life of its own: it is written through
    ///         its parent, inside the parent's transaction, behind the parent's permissions, validation
    ///         and events. <c>[Resource]</c> says the opposite — that it is addressable on its own, with
    ///         its own create, update and delete endpoints. Both on one type is not a rich configuration,
    ///         it is two declarations that cannot both be true.
    ///     </para>
    ///     <para>
    ///         Without it the second one would simply win: the generator would scaffold the full CRUD
    ///         and say nothing, so a child aggregate could be created and deleted over HTTP without ever
    ///         passing through the parent — exactly what <c>[PartOf]</c> is there to prevent.
    ///     </para>
    ///     <para>
    ///         Reported from Resource because Resource is what emits the surface — the range belongs to
    ///         whoever raises the diagnostic, not to whoever inspired it. The mutation half of the same
    ///         contradiction is PRAG0438, raised by Actions for the same reason.
    ///     </para>
    /// </remarks>
    public static readonly DiagnosticDescriptor ResourceOnAggregatePart = DiagnosticFactory.Error(
        "PRAG2611",
        "This entity is part of another aggregate and cannot be a resource",
        "'{0}' declares [PartOf<{1}>] — it is written through '{1}' — and [Resource(\"{2}\")], which "
        + "would give it create, update and delete endpoints of its own, bypassing '{1}'.",
        "Keep [PartOf<TParent>] and drop [Resource] if the entity belongs to its parent's aggregate; "
        + "drop [PartOf<TParent>] if it has a life, and operations, of its own.");
}
