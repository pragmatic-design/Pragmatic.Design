namespace Pragmatic.Documents.Templates.Nodes;

/// <summary>Heading template — content can contain <c>{{expressions}}</c>.</summary>
public sealed record HeadingTemplate : DocumentNodeTemplate
{
    public int Level { get; init; } = 1;
    public required string Content { get; init; }
}
