namespace Pragmatic.Documents.Templating.Expressions;

/// <summary>Expression segment (inside <c>{{ }}</c>).</summary>
public sealed record ExpressionSegment(TemplateExpression Expression) : InterpolatedSegment;
