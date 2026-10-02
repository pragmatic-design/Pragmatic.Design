namespace Pragmatic.Email.Templates.Nodes;

/// <summary>References a reusable email partial by name.</summary>
public sealed record EmailPartialTemplate : EmailNodeTemplate
{
    public required string Name { get; init; }
}
