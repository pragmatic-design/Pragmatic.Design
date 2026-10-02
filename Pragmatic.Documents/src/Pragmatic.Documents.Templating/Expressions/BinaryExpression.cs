namespace Pragmatic.Documents.Templating.Expressions;

/// <summary>Binary operation: <c>a + b</c>, <c>a > b</c>, <c>a == b</c>, <c>a &amp;&amp; b</c></summary>
public sealed record BinaryExpression(
    TemplateExpression Left,
    BinaryOp Operator,
    TemplateExpression Right) : TemplateExpression;
