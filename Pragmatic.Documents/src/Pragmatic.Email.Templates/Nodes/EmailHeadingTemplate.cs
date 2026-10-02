using Pragmatic.Email.Model;

namespace Pragmatic.Email.Templates.Nodes;

/// <summary>Heading — content can contain <c>{{expressions}}</c>.</summary>
public sealed record EmailHeadingTemplate : EmailNodeTemplate
{
    public int Level { get; init; } = 1;
    public required string Content { get; init; }
    public string? Color { get; init; }
    public EmailTextAlign Align { get; init; } = EmailTextAlign.Left;
}
