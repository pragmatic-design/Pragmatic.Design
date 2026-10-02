namespace Pragmatic.Documents.Model;

/// <summary>
/// CSS-like style properties applicable to any document node.
/// All dimensions in mm unless noted.
/// </summary>
/// <remarks>
///     <para>
///         The typography properties are run formatting, and they cascade as in CSS: set on a
///         <see cref="HeadingNode" />, <see cref="ParagraphNode" /> or <see cref="HyperlinkNode" />, they apply
///         to the text inside it, and a <see cref="TextNode" />'s own style wins, property by property, where
///         both set one. Both renderers apply the rule; the PDF engine does not map
///         <see cref="LetterSpacing" />.
///     </para>
///     <para>
///         The layout properties — alignment, spacing, indents — are the block's own and do not cascade.
///     </para>
/// </remarks>
public sealed record NodeStyle
{
    // --- Spacing ---
    public double? MarginTop { get; init; }
    public double? MarginBottom { get; init; }
    public double? MarginLeft { get; init; }
    public double? MarginRight { get; init; }
    public double? PaddingTop { get; init; }
    public double? PaddingBottom { get; init; }
    public double? PaddingLeft { get; init; }
    public double? PaddingRight { get; init; }

    // --- Typography ---
    public string? FontFamily { get; init; }
    /// <summary>Font size in points.</summary>
    public double? FontSize { get; init; }
    public FontWeight? FontWeight { get; init; }
    public bool? Italic { get; init; }
    public bool? Underline { get; init; }
    public bool? Strikethrough { get; init; }
    public VerticalPosition? VerticalPosition { get; init; }
    public string? Color { get; init; }
    /// <summary>Highlight/background color for text runs (OOXML highlight color name or hex).</summary>
    public string? HighlightColor { get; init; }
    /// <summary>Letter spacing in mm.</summary>
    public double? LetterSpacing { get; init; }

    // --- Layout ---
    public TextAlign? TextAlign { get; init; }
    public double? Width { get; init; }
    public double? Height { get; init; }
    /// <summary>Line height as a multiplier (1.0 = single, 1.5, 2.0 = double).</summary>
    public double? LineHeight { get; init; }
    /// <summary>First line indent in mm.</summary>
    public double? FirstLineIndent { get; init; }

    // --- Border ---
    public string? BorderColor { get; init; }
    public double? BorderWidth { get; init; }
    public BorderStyle? BorderStyle { get; init; }

    // --- Background ---
    public string? BackgroundColor { get; init; }

    // --- Table cell ---
    public CellVerticalAlign? CellVerticalAlign { get; init; }
}
