namespace Pragmatic.Documents.Templating.Expressions;

/// <summary>A literal value (string, number, bool, null).</summary>
public sealed record LiteralExpression(object? Value) : TemplateExpression;
