namespace Pragmatic.Documents.Ooxml;

/// <summary>
/// Color and font definitions for OOXML theme1.xml.
/// Shared between DOCX and XLSX.
/// </summary>
public sealed record OoxmlThemeColors
{
    /// <summary>Primary dark color (dk1) — usually text color.</summary>
    public string TextColor { get; init; } = "000000";

    /// <summary>Primary light color (lt1) — usually background.</summary>
    public string BackgroundColor { get; init; } = "FFFFFF";

    /// <summary>Secondary dark color (dk2).</summary>
    public string SecondaryColor { get; init; } = "1F3864";

    /// <summary>Accent color (accent1) — basis for accent2-6 auto-derivation.</summary>
    public string AccentColor { get; init; } = "4472C4";

    /// <summary>Hyperlink color.</summary>
    public string HyperlinkColor { get; init; } = "0563C1";

    /// <summary>Heading font family (major font).</summary>
    public string HeadingFont { get; init; } = "Calibri Light";

    /// <summary>Body font family (minor font).</summary>
    public string BodyFont { get; init; } = "Calibri";

    /// <summary>Default theme colors.</summary>
    public static OoxmlThemeColors Default { get; } = new();
}
