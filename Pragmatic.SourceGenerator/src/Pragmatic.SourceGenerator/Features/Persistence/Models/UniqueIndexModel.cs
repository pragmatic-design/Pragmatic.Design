using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     One unique index declared with <c>[Unique]</c>.
/// </summary>
/// <remarks>
///     Separate from the domain key: <c>[LogicKey]</c> produces one index and an accessor to go with
///     it, while an entity can carry any number of these and gets no accessor for them. The two travel
///     together through every template that emits schema, which is why the tenant column is decided
///     the same way for both.
/// </remarks>
internal sealed record UniqueIndexModel
{
    /// <summary>The properties the index covers, in the order written.</summary>
    public required EquatableArray<string> Columns { get; init; }

    /// <summary>Whether the index asked for uniqueness across every tenant rather than within one.</summary>
    public bool IsGlobal { get; init; }
}
