namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     The read strategy a query declares with <c>[QueryStrategy]</c>.
/// </summary>
/// <remarks>
///     Mirrors <c>Pragmatic.Persistence.Query.QueryStrategy</c> member for member and value for value:
///     the attribute argument arrives as the underlying <c>int</c>, so a member reordered on one side
///     and not the other would silently decode as a different, valid strategy.
/// </remarks>
internal enum QueryStrategyKind
{
    Projection = 0,
    Entity = 1,
    Filtered = 2,
    Raw = 3
}
