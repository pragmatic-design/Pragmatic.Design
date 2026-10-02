namespace Pragmatic.Documents.Templates.Nodes;

/// <summary>Text node — content can contain <c>{{expressions}}</c>.</summary>
public sealed record TextTemplate : DocumentNodeTemplate
{
    public required string Content { get; init; }
}
