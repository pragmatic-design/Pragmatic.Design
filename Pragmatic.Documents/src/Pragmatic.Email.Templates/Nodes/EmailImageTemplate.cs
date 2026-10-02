using Pragmatic.Email.Model;

namespace Pragmatic.Email.Templates.Nodes;

/// <summary>Image — Source can contain <c>{{expressions}}</c>.</summary>
public sealed record EmailImageTemplate : EmailNodeTemplate
{
    public required string Source { get; init; }
    public required string Alt { get; init; }
    public int? Width { get; init; }
    public int? Height { get; init; }
    public string? Link { get; init; }
    public EmailTextAlign Align { get; init; } = EmailTextAlign.Center;
}
