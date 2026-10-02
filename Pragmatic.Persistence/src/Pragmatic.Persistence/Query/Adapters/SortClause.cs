namespace Pragmatic.Persistence.Query.Adapters;

/// <summary>
///     A sort specification in a <see cref="GridFilterRequest" />.
/// </summary>
/// <param name="Field">The entity property name to sort by.</param>
/// <param name="Direction">The sort direction.</param>
public record SortClause(string Field, SortDirection Direction);
