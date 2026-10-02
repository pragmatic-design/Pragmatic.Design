namespace Pragmatic.Documents.Model;

/// <summary>Inline text span.</summary>
public sealed record TextNode : DocumentNode
{
    public required string Content { get; init; }
}
