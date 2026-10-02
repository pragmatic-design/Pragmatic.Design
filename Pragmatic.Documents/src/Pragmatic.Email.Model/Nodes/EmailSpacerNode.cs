namespace Pragmatic.Email.Model;

/// <summary>Vertical spacer in an email.</summary>
public sealed record EmailSpacerNode : EmailNode
{
    /// <summary>Height in pixels.</summary>
    public int Height { get; init; } = 20;
}
