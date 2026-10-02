using Pragmatic.Email.Model;

namespace Pragmatic.Email.Templates.Nodes;

/// <summary>Template for an email table cell — Content can contain <c>{{expressions}}</c>.</summary>
public sealed record EmailTableCellTemplate
{
    public string Content { get; init; } = "";
    public int ColSpan { get; init; } = 1;
    public bool Bold { get; init; }
    public string? Color { get; init; }
    public EmailTextAlign? Align { get; init; }
}
