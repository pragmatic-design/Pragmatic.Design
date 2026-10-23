namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>A declared query's filter: the condition on the entity <c>e</c>, composed when <see cref="Guard" /> holds (always when null).</summary>
internal sealed record QueryConditionModel(string? Guard, string Condition);
