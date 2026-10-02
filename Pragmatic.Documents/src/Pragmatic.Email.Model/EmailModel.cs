namespace Pragmatic.Email.Model;

/// <summary>
/// Root of an email template. Contains metadata, preheader, and sections.
/// This is the JSON contract for HTML email rendering (table-based, inline CSS, 600px).
/// Separate from <c>DocumentModel</c> — emails and documents are different worlds.
/// </summary>
public sealed record EmailModel
{
    /// <summary>Email subject line.</summary>
    public string? Subject { get; init; }

    /// <summary>Preheader text (preview text in email clients).</summary>
    public string? Preheader { get; init; }

    /// <summary>Language (BCP-47, e.g. "it-IT").</summary>
    public string? Language { get; init; }

    /// <summary>Email body width in pixels. Default 600.</summary>
    public int Width { get; init; } = 600;

    /// <summary>Body background color (hex).</summary>
    public string BackgroundColor { get; init; } = "#ffffff";

    /// <summary>Outer wrapper background color (hex).</summary>
    public string? WrapperBackgroundColor { get; init; }

    /// <summary>Default font family.</summary>
    public string FontFamily { get; init; } = "Arial, Helvetica, sans-serif";

    /// <summary>Default font size in px.</summary>
    public int FontSize { get; init; } = 16;

    /// <summary>Default text color (hex).</summary>
    public string TextColor { get; init; } = "#333333";

    /// <summary>The email sections (stacked vertically).</summary>
    public IReadOnlyList<EmailSection> Sections { get; init; } = [];
}
