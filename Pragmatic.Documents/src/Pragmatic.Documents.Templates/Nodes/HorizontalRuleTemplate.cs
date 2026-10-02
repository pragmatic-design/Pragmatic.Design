namespace Pragmatic.Documents.Templates.Nodes;

/// <summary>Horizontal rule template.</summary>
public sealed record HorizontalRuleTemplate : DocumentNodeTemplate
{
    public double Thickness { get; init; } = 0.5;
}
