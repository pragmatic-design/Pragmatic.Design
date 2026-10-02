namespace Pragmatic.SourceGenerator.Features.Resource.Models;

/// <summary>
///     A <c>[ReturnsDto&lt;T&gt;]</c> a developer put on the partial part of a scaffolded operation.
/// </summary>
internal sealed record ResourceDeclaredDto
{
    /// <summary>Namespace + name of the operation it decorates — the identity it is matched by.</summary>
    public required string OperationKey { get; init; }

    /// <summary>The DTO's namespace-qualified name, without <c>global::</c>.</summary>
    public required string DtoFullTypeName { get; init; }

    /// <summary>The DTO's name on its own.</summary>
    public required string DtoTypeName { get; init; }

    /// <summary>
    ///     Whether the DTO carries <c>[MapFrom]</c> and <c>[GenerateProjection]</c>. False is PRAG2608:
    ///     a read query projects in the database, and a DTO with no projection falls back to
    ///     <c>OfType&lt;TResult&gt;()</c> — which matches nothing and answers an empty result instead of
    ///     an error.
    /// </summary>
    public bool HasProjection { get; init; }

    /// <summary>The entity <c>[MapFrom]</c> names, for PRAG2609 when it is not the resource's own.</summary>
    public string? MapsFromEntity { get; init; }
}
