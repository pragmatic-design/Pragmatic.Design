using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Resource.Models;

/// <summary>
/// Immutable model for a [Resource]-annotated entity.
/// Extracted by <see cref="Transforms.ResourceTransform"/> from attribute syntax.
/// </summary>
internal sealed record ResourceModel
{
    /// <summary>The entity namespace.</summary>
    public required string Namespace { get; init; }

    /// <summary>The entity type name (e.g. "Reservation").</summary>
    public required string TypeName { get; init; }

    /// <summary>Fully qualified entity type name.</summary>
    public required string FullTypeName { get; init; }

    /// <summary>The URL route segment (e.g. "reservations").</summary>
    public required string Segment { get; init; }

    /// <summary>Flags enum for auto-CRUD generation.</summary>
    public int Capabilities { get; init; }

    /// <summary>Route parameter name override (e.g. "reservationId"). Null = auto-derived.</summary>
    public string? ParamName { get; init; }

    /// <summary>
    ///     The boundary type, <b>fully qualified</b> (<c>global::Showcase.Booking.BookingBoundary</c>) —
    ///     the format every model in the pipeline uses for a type name, and the one the boundary facade
    ///     compares against.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Not the plain display string <c>BoundaryOwnershipReader.BoundaryOf</c> returns. With that,
    ///     every consumer would have to prefix <c>global::</c> itself, and one that did not would fail:
    ///     <c>MatchMembersToBoundary</c> compares this against the boundary's own fully qualified name
    ///     as a string, and a member that names a boundary is never matched by namespace, so the
    ///     scaffolded mutations would land on no facade at all.
    /// </remarks>
    public string? BoundaryFullTypeName { get; init; }

    /// <summary>The boundary short name (e.g. "Booking").</summary>
    public string? BoundaryName { get; init; }

    /// <summary>Entity Id type as fully qualified string (e.g. "System.Guid").</summary>
    public required string IdType { get; init; }

    /// <summary>Location for diagnostic reporting.</summary>
    /// <summary>
    ///     The parent named by <c>[PartOf&lt;TParent&gt;]</c> on the same entity, when there is one.
    ///     Its presence contradicts this resource — see PRAG2611.
    /// </summary>
    public string? PartOfParentTypeName { get; init; }

    /// <summary>
    ///     Whether a trait on the same type consumes this resource's segment as its route prefix.
    /// </summary>
    /// <remarks>
    ///     ⚠️ A resource with no capabilities scaffolds no CRUD — and that is the documented way to
    ///     give <c>[HasTags]</c>, <c>[HasComments]</c>, <c>[HasNotes]</c> or <c>[HasAttachments]</c>
    ///     the segment their endpoints hang under. PRAG2612 read that case as "scaffolds nothing" and
    ///     advised removing the attribute, which is what PRAG2601 exists to complain about: between
    ///     the two there was no way to write such an entity without a warning.
    /// </remarks>
    public bool HasRouteConsumingTrait { get; init; }

    public LocationInfo? LocationInfo { get; init; }
    public Location? Location => LocationInfo?.ToLocation();

    /// <summary>Computed: the route parameter name. Falls back to {singularSegment}Id.</summary>
    public string ResolvedParamName => ParamName ?? DeriveSingularParam();

    private string DeriveSingularParam()
    {
        var seg = Segment.TrimEnd('s');
        // kebab-case → camelCase: "room-type" → "roomType"
        if (seg.Contains('-'))
        {
            var parts = seg.Split('-');
            return parts[0] + string.Concat(parts.Skip(1).Select(Capitalize)) + "Id";
        }

        return seg + "Id";
    }

    private static string Capitalize(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
}
