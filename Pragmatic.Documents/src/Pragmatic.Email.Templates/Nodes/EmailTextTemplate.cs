using Pragmatic.Email.Model;

namespace Pragmatic.Email.Templates.Nodes;

/// <summary>Text — content can contain <c>{{expressions}}</c>.</summary>
public sealed record EmailTextTemplate : EmailNodeTemplate
{
    public required string Content { get; init; }
    public int? FontSize { get; init; }
    public string? Color { get; init; }
    public EmailTextAlign Align { get; init; } = EmailTextAlign.Left;
}
