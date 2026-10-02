using Pragmatic.Email.Model;

namespace Pragmatic.Email.Templates;

/// <summary>
/// Root email template. Parallel to <see cref="EmailModel"/> with expression support.
/// </summary>
public sealed record EmailTemplate
{
    /// <summary>Subject — can contain <c>{{expressions}}</c>.</summary>
    public string? Subject { get; init; }

    /// <summary>Preheader — can contain <c>{{expressions}}</c>.</summary>
    public string? Preheader { get; init; }

    public string? Language { get; init; }
    public int Width { get; init; } = 600;
    public string BackgroundColor { get; init; } = "#ffffff";
    public string? WrapperBackgroundColor { get; init; }
    public string FontFamily { get; init; } = "Arial, Helvetica, sans-serif";
    public int FontSize { get; init; } = 16;
    public string TextColor { get; init; } = "#333333";

    public IReadOnlyList<EmailSectionTemplate> Sections { get; init; } = [];
}
