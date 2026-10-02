namespace Pragmatic.Documents.Docx;

/// <summary>
/// Design tokens for DOCX document styling.
/// Controls fonts, colors, heading sizes, and spacing across the entire document.
/// Feeds into styles.xml and theme1.xml generation.
/// </summary>
public sealed record DocxTheme
{
    // --- Fonts ---

    /// <summary>Body text font family.</summary>
    public string BodyFont { get; init; } = "Calibri";

    /// <summary>Heading font family.</summary>
    public string HeadingFont { get; init; } = "Calibri Light";

    // --- Colors (hex without #) ---

    /// <summary>Primary heading color (H1-H2).</summary>
    public string PrimaryColor { get; init; } = "2F5496";

    /// <summary>Secondary heading color (H3+).</summary>
    public string SecondaryColor { get; init; } = "1F3864";

    /// <summary>Body text color.</summary>
    public string TextColor { get; init; } = "000000";

    /// <summary>Document background color.</summary>
    public string BackgroundColor { get; init; } = "FFFFFF";

    /// <summary>Accent color for tables, emphasis, charts.</summary>
    public string AccentColor { get; init; } = "4472C4";

    /// <summary>Hyperlink color.</summary>
    public string HyperlinkColor { get; init; } = "0563C1";

    // --- Typography (half-points: 22 = 11pt, 24 = 12pt) ---

    /// <summary>Body text font size in half-points.</summary>
    public int BodyFontSize { get; init; } = 22;

    /// <summary>H1 font size in half-points.</summary>
    public int H1FontSize { get; init; } = 32;

    /// <summary>H2 font size in half-points.</summary>
    public int H2FontSize { get; init; } = 26;

    /// <summary>H3 font size in half-points.</summary>
    public int H3FontSize { get; init; } = 24;

    /// <summary>H4-H6 font size in half-points.</summary>
    public int H4FontSize { get; init; } = 22;

    /// <summary>Whether H1 and H2 are bold.</summary>
    public bool HeadingBold { get; init; } = true;

    // --- Spacing (twips) ---

    /// <summary>Default paragraph spacing after (twips). 160 = 8pt.</summary>
    public int DefaultSpacingAfter { get; init; } = 160;

    /// <summary>Default line spacing (twips). 259 = 1.08x.</summary>
    public int DefaultLineSpacing { get; init; } = 259;

    /// <summary>H1 spacing before (twips).</summary>
    public int H1SpacingBefore { get; init; } = 240;

    /// <summary>H1 spacing after (twips).</summary>
    public int H1SpacingAfter { get; init; } = 120;

    /// <summary>H2 spacing before (twips).</summary>
    public int H2SpacingBefore { get; init; } = 200;

    /// <summary>H2 spacing after (twips).</summary>
    public int H2SpacingAfter { get; init; } = 80;

    /// <summary>H3 spacing before (twips).</summary>
    public int H3SpacingBefore { get; init; } = 160;

    /// <summary>H3 spacing after (twips).</summary>
    public int H3SpacingAfter { get; init; } = 60;

    // --- Heading color helper ---

    /// <summary>Get the heading color for a given level.</summary>
    public string HeadingColor(int level) => level <= 2 ? PrimaryColor : level <= 4 ? SecondaryColor : TextColor;

    /// <summary>Get the heading font size for a given level.</summary>
    public int HeadingFontSize(int level) => level switch
    {
        1 => H1FontSize,
        2 => H2FontSize,
        3 => H3FontSize,
        _ => H4FontSize
    };

    /// <summary>Get heading spacing before for a given level.</summary>
    public (int Before, int After) HeadingSpacing(int level) => level switch
    {
        1 => (H1SpacingBefore, H1SpacingAfter),
        2 => (H2SpacingBefore, H2SpacingAfter),
        3 => (H3SpacingBefore, H3SpacingAfter),
        _ => (80, 40)
    };

    // --- Presets ---

    /// <summary>Default theme (matches Word's default).</summary>
    public static DocxTheme Default { get; } = new();

    /// <summary>Formal theme (serif fonts, muted colors).</summary>
    public static DocxTheme Formal { get; } = new()
    {
        BodyFont = "Times New Roman",
        HeadingFont = "Georgia",
        PrimaryColor = "333333",
        SecondaryColor = "555555",
        AccentColor = "7B7B7B",
        HyperlinkColor = "2E74B5",
        BodyFontSize = 24,
        H1FontSize = 36,
        H2FontSize = 28,
        H3FontSize = 26,
        DefaultLineSpacing = 276 // 1.15x
    };

    /// <summary>Modern theme (clean sans-serif, vibrant colors).</summary>
    public static DocxTheme Modern { get; } = new()
    {
        BodyFont = "Segoe UI",
        HeadingFont = "Segoe UI Semibold",
        PrimaryColor = "0078D4",
        SecondaryColor = "005A9E",
        AccentColor = "0078D4",
        HyperlinkColor = "0078D4",
        HeadingBold = false,
        DefaultLineSpacing = 276
    };

    /// <summary>Minimal theme (monochrome, tight spacing).</summary>
    public static DocxTheme Minimal { get; } = new()
    {
        BodyFont = "Arial",
        HeadingFont = "Arial",
        PrimaryColor = "222222",
        SecondaryColor = "444444",
        AccentColor = "666666",
        HyperlinkColor = "0066CC",
        H1FontSize = 28,
        H2FontSize = 24,
        H3FontSize = 22,
        DefaultSpacingAfter = 120,
        DefaultLineSpacing = 240, // 1.0x single
        H1SpacingBefore = 160,
        H1SpacingAfter = 80,
        H2SpacingBefore = 120,
        H2SpacingAfter = 60
    };
}
