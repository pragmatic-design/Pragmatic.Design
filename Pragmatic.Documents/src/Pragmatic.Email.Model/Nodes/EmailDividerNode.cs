namespace Pragmatic.Email.Model;

/// <summary>Horizontal divider line in an email.</summary>
public sealed record EmailDividerNode : EmailNode
{
    /// <summary>Divider color (hex).</summary>
    public string Color { get; init; } = "#cccccc";

    /// <summary>Thickness in pixels.</summary>
    public int Thickness { get; init; } = 1;
}
