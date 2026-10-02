namespace Pragmatic.Documents.Templating.Expressions;

/// <summary>Null coalescing: <c>value ?? fallback</c></summary>
public sealed record NullCoalescingExpression(
    TemplateExpression Left,
    TemplateExpression Right) : TemplateExpression;
