namespace Pragmatic.Email.Templates.Nodes;

/// <summary>Spacer.</summary>
public sealed record EmailSpacerTemplate : EmailNodeTemplate
{
    public int Height { get; init; } = 20;
}
