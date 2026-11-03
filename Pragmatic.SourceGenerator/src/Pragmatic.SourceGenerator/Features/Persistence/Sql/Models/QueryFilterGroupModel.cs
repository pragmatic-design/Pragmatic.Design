using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     A filter object of a declared query (<c>[ComplexFilter]</c>, and the <c>[FilterGroup]</c>s inside it): composed when <see cref="Guard" />
///     holds; its <see cref="Conditions" />, then its <see cref="Groups" />, joined with OR when <see cref="Or" />, else with AND, as Pragmatic's
///     <c>[FilterDto]</c> joins them.
/// </summary>
internal sealed record QueryFilterGroupModel(string Guard, bool Or, EquatableArray<QueryConditionModel> Conditions, EquatableArray<QueryFilterGroupModel> Groups);
