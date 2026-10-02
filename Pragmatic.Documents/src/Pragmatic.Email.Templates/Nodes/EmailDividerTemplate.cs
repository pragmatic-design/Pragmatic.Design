namespace Pragmatic.Email.Templates.Nodes;

/// <summary>Divider.</summary>
public sealed record EmailDividerTemplate : EmailNodeTemplate
{
    public string Color { get; init; } = "#cccccc";
    public int Thickness { get; init; } = 1;
}
