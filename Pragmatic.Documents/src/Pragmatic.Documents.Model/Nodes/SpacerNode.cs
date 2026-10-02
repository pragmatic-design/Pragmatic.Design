namespace Pragmatic.Documents.Model;

/// <summary>Vertical spacer element.</summary>
public sealed record SpacerNode : DocumentNode
{
    /// <summary>Height in mm.</summary>
    public double Height { get; init; } = 10;
}
