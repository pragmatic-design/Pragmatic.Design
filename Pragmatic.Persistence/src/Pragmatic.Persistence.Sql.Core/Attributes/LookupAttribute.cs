namespace Pragmatic.Persistence.Sql.Attributes;

/// <summary>
///     A lookup table, small and rarely changing (the repository's <c>[Lookup]</c>, read by name: its own attribute works the same): read whole on
///     purpose, so an unbounded read of it is not reported (PRAG3021), as <see cref="TableAttribute.Small" /> says.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LookupAttribute : Attribute;
