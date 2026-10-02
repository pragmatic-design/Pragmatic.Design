namespace Pragmatic.Documents.Templating.Expressions;

/// <summary>Pipe application: <c>value | pipeName:"arg1","arg2"</c></summary>
public sealed record PipeExpression(
    TemplateExpression Input,
    string PipeName,
    IReadOnlyList<string> Args) : TemplateExpression;
