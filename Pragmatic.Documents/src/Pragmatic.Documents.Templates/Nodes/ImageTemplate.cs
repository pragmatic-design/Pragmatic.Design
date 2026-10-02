namespace Pragmatic.Documents.Templates.Nodes;

/// <summary>Image template — Source can contain <c>{{expressions}}</c>.</summary>
public sealed record ImageTemplate : DocumentNodeTemplate
{
    public required string Source { get; init; }
    public string? Alt { get; init; }
    public double? Width { get; init; }
    public double? Height { get; init; }
}
