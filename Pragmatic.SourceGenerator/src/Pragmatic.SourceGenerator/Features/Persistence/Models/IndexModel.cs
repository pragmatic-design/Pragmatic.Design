using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     One non-unique index declared with <c>[Index]</c> (#524): the columns as written, with no tenant column added and no filter, where a
///     <see cref="UniqueIndexModel" /> is led by the tenant. It travels through the same templates a unique index does.
/// </summary>
internal sealed record IndexModel
{
    /// <summary>The properties the index covers, in the order written.</summary>
    public required EquatableArray<string> Columns { get; init; }
}
