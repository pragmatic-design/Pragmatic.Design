namespace Pragmatic.Documents.Templating.Expressions;

/// <summary>Ternary: <c>condition ? trueValue : falseValue</c></summary>
public sealed record TernaryExpression(
    TemplateExpression Condition,
    TemplateExpression TrueValue,
    TemplateExpression FalseValue) : TemplateExpression;
