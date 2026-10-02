namespace Pragmatic.Documents.Templating.Expressions;

/// <summary>Interpolated string with mixed literal + expression segments: <c>Hello {{name}}, total: {{total | currency}}</c></summary>
public sealed record InterpolatedStringExpression(
    IReadOnlyList<InterpolatedSegment> Segments) : TemplateExpression;
