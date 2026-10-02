namespace Pragmatic.Email.Model;

/// <summary>Heading in an email (H1-H6 rendered with inline styles).</summary>
public sealed record EmailHeadingNode : EmailNode
{
    /// <summary>Heading level (1-6).</summary>
    public int Level { get; init; } = 1;

    /// <summary>Heading text.</summary>
    public required string Content { get; init; }

    /// <summary>Text color (hex). Null = inherit.</summary>
    public string? Color { get; init; }

    /// <summary>Text alignment.</summary>
    public EmailTextAlign Align { get; init; } = EmailTextAlign.Left;
}
