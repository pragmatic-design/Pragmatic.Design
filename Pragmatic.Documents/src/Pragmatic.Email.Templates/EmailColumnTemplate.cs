using Pragmatic.Email.Model;

namespace Pragmatic.Email.Templates;

/// <summary>Email column template.</summary>
public sealed record EmailColumnTemplate
{
    public double Width { get; init; } = 1.0;
    public EmailVerticalAlign VerticalAlign { get; init; } = EmailVerticalAlign.Top;
    public EmailPadding? Padding { get; init; }
    public IReadOnlyList<Nodes.EmailNodeTemplate> Content { get; init; } = [];
}
