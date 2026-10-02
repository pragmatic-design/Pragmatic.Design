namespace Pragmatic.Email.Model;

/// <summary>Call-to-action button (rendered as a bulletproof table-based button for email).</summary>
public sealed record EmailButtonNode : EmailNode
{
    /// <summary>Button text.</summary>
    public required string Text { get; init; }

    /// <summary>Button link URL.</summary>
    public required string Href { get; init; }

    /// <summary>Button background color (hex).</summary>
    public string BackgroundColor { get; init; } = "#007bff";

    /// <summary>Button text color (hex).</summary>
    public string TextColor { get; init; } = "#ffffff";

    /// <summary>Border radius in px.</summary>
    public int BorderRadius { get; init; } = 4;

    /// <summary>Font size in px.</summary>
    public int FontSize { get; init; } = 16;

    /// <summary>Alignment.</summary>
    public EmailTextAlign Align { get; init; } = EmailTextAlign.Center;

    /// <summary>Padding (internal) in px.</summary>
    public EmailPadding Padding { get; init; } = new() { Top = 12, Right = 24, Bottom = 12, Left = 24 };
}
