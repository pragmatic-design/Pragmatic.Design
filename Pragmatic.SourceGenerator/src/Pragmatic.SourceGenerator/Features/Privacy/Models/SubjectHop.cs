namespace Pragmatic.SourceGenerator.Features.Privacy.Models;

/// <summary>
///     One hop of a <see cref="SubjectRoute" />, by key: the rows of <see cref="FromFullTypeName" /> whose <see cref="ForeignKey" /> is a key of
///     <see cref="ToFullTypeName" />. An engine's adapter follows a route this way, because a document keeps a relation as its key and has no
///     navigation to follow.
/// </summary>
/// <remarks>Not part of any incremental model, as <see cref="SubjectRoute" />.</remarks>
internal sealed class SubjectHop
{
    /// <summary>The entity the hop starts from, fully qualified (no <c>global::</c>).</summary>
    public required string FromFullTypeName { get; init; }

    /// <summary>The foreign key of the navigation on <see cref="FromFullTypeName" />; null when the relation declares none.</summary>
    public required string? ForeignKey { get; init; }

    /// <summary>The key's type, with <c>global::</c>; null with <see cref="ForeignKey" />.</summary>
    public required string? ForeignKeyType { get; init; }

    /// <summary>The entity the hop leads to, fully qualified (no <c>global::</c>).</summary>
    public required string ToFullTypeName { get; init; }
}
