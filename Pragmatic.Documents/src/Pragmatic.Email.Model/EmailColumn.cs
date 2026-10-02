namespace Pragmatic.Email.Model;

/// <summary>
/// A column within an email section.
/// Width is expressed as a fraction (e.g. 1/2 = 50%, 1/3 = 33%).
/// </summary>
public sealed record EmailColumn
{
    /// <summary>Width as fraction of parent (0.0-1.0). Default 1.0 (full width).</summary>
    public double Width { get; init; } = 1.0;

    /// <summary>Vertical alignment within the row.</summary>
    public EmailVerticalAlign VerticalAlign { get; init; } = EmailVerticalAlign.Top;

    /// <summary>Padding for this column.</summary>
    public EmailPadding? Padding { get; init; }

    /// <summary>Content nodes within this column.</summary>
    public IReadOnlyList<EmailNode> Content { get; init; } = [];
}
