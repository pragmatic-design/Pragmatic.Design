namespace Pragmatic.Documents.Templating.Data;

/// <summary>
/// Warning emitted during template resolution (e.g., unresolved property path).
/// </summary>
public sealed record TemplateWarning(string Path, string Message)
{
    public override string ToString() => $"{Path}: {Message}";
}
