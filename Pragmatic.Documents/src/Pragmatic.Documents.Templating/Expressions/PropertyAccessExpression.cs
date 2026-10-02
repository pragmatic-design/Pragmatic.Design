namespace Pragmatic.Documents.Templating.Expressions;

/// <summary>Property access path: <c>customer.address.city</c></summary>
public sealed record PropertyAccessExpression(string Path) : TemplateExpression;
