namespace Pragmatic.Documents.Templating.Expressions;

/// <summary>Translation lookup: <c>t:key</c> or <c>t:key(param=expr)</c></summary>
public sealed record TranslateExpression(
    string Key,
    IReadOnlyDictionary<string, TemplateExpression>? Params) : TemplateExpression;
