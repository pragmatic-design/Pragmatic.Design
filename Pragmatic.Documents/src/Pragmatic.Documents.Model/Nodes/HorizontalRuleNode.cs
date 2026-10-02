namespace Pragmatic.Documents.Model;

/// <summary>Horizontal rule / separator line.</summary>
public sealed record HorizontalRuleNode : DocumentNode
{
    /// <summary>Line thickness in mm. Default 0.5.</summary>
    public double Thickness { get; init; } = 0.5;
}
