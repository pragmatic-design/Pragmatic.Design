namespace Pragmatic.Documents.Templating.Expressions;

/// <summary>Aggregate function: <c>items.sum(price)</c>, <c>items.count</c></summary>
public sealed record AggregateExpression(
    string CollectionPath,
    AggregateFunction Function,
    string? PropertyName) : TemplateExpression;
