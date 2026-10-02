using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     A trait whose properties the entity declares only part of, carried to output time as PRAG0624.
/// </summary>
/// <remarks>
///     The generator emits a trait's properties as a group and can only stand down on the group, so
///     declaring some of one is neither owning it nor leaving it: all of them are emitted, and each one
///     the author wrote collides with a generated twin. Found during the transform, where the entity's
///     members are symbols; reported from the source-output stage, which is why this holds values
///     rather than a <c>Diagnostic</c>.
/// </remarks>
internal sealed record PartialTraitModel
{
    /// <summary>The attribute name, without the suffix — <c>Auditable</c>, <c>SoftDelete</c>.</summary>
    public required string TraitName { get; init; }

    /// <summary>The properties of the group the entity has not declared.</summary>
    public required EquatableArray<string> MissingProperties { get; init; }
}
