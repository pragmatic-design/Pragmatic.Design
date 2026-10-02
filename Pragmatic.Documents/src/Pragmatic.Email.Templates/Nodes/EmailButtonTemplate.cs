using Pragmatic.Email.Model;

namespace Pragmatic.Email.Templates.Nodes;

/// <summary>Button — Text and Href can contain <c>{{expressions}}</c>.</summary>
public sealed record EmailButtonTemplate : EmailNodeTemplate
{
    public required string Text { get; init; }
    public required string Href { get; init; }
    public string BackgroundColor { get; init; } = "#007bff";
    public string TextColor { get; init; } = "#ffffff";
    public int BorderRadius { get; init; } = 4;
    public int FontSize { get; init; } = 16;
    public EmailTextAlign Align { get; init; } = EmailTextAlign.Center;
}
