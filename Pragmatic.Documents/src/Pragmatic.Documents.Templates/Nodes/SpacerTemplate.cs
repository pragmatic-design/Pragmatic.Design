namespace Pragmatic.Documents.Templates.Nodes;

/// <summary>Spacer template.</summary>
public sealed record SpacerTemplate : DocumentNodeTemplate
{
    public double Height { get; init; } = 10;
}
