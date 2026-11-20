namespace Pragmatic.Persistence.Documents.Schema;

/// <summary>
///     A range a collection's documents must hold (#900): <paramref name="To" /> null, or not before <paramref name="From" />. A temporal
///     relation's <c>ValidFrom</c> and <c>ValidTo</c>, the Sql engine's and EF Core's <c>CK_{Table}_ValidRange</c>.
/// </summary>
/// <param name="Name">The rule's name, as the Sql schema names its check (<c>CK_{collection}_ValidRange</c>): what the schema's report names.</param>
/// <param name="From">The stored name of the range's start.</param>
/// <param name="To">The stored name of its end, which may be null.</param>
public sealed record DocRange(string Name, string From, string To);
