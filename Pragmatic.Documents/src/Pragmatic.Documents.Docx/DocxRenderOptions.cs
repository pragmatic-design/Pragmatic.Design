namespace Pragmatic.Documents.Docx;

/// <summary>
/// Options for DOCX rendering.
/// </summary>
public sealed record DocxRenderOptions
{
    /// <summary>
    /// When true, Word will prompt to update fields (TOC, page numbers) on open.
    /// When false, the TOC is pre-populated with estimated page numbers via heuristic.
    /// Default: true.
    /// </summary>
    public bool UpdateFieldsOnOpen { get; init; } = true;

    /// <summary>
    /// Document theme controlling fonts, colors, and spacing.
    /// When null, <see cref="DocxTheme.Default"/> is used.
    /// Ignored when <see cref="Template"/> is set (template styles take precedence).
    /// </summary>
    public DocxTheme? Theme { get; init; }

    /// <summary>
    /// A .dotx template file (as bytes). When set, styles, theme, and font table
    /// are extracted from the template instead of being generated.
    /// Takes precedence over <see cref="Theme"/>.
    /// </summary>
    public byte[]? Template { get; init; }

    /// <summary>
    /// Timestamp used to render DATE/TIME field placeholders. Inject a fixed value for
    /// deterministic output (e.g. tests, reproducible builds). When null, the render-time
    /// clock (<see cref="DateTimeOffset.Now"/>) is used at the moment of rendering.
    /// Note: these are only placeholders — Word recalculates the field on open.
    /// </summary>
    public DateTimeOffset? RenderTimestamp { get; init; }

    /// <summary>
    /// Resolve the effective theme (<see cref="Theme"/> or <see cref="DocxTheme.Default"/>).
    /// Ignored when <see cref="Template"/> is set — the template carries its own embedded theme.
    /// </summary>
    internal DocxTheme EffectiveTheme => Theme ?? DocxTheme.Default;

    /// <summary>Default options.</summary>
    public static DocxRenderOptions Default { get; } = new();
}
