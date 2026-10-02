namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Represents a collection-of-primitives property on an entity (e.g. <c>List&lt;string&gt;</c>,
///     <c>string[]</c>, <c>IEnumerable&lt;int&gt;</c>). EF Core 8+ persists these as a JSON
///     "primitive collection" column rather than a navigation, so they are configured with
///     <c>builder.Property(...)</c> in the generated EntityConfiguration.
/// </summary>
internal sealed record PrimitiveCollectionMetadataModel
{
    /// <summary>
    ///     The property name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    ///     Whether the collection reference itself is nullable (e.g. <c>List&lt;string&gt;?</c>).
    /// </summary>
    public bool IsNullable { get; init; }
}
