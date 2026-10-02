namespace Pragmatic.Documents.Model;

/// <summary>
/// Table of Contents placeholder. Renderers generate TOC based on heading levels.
/// In DOCX, this produces a field code that Word evaluates on open.
/// In PDF/Typst, this produces #outline().
/// </summary>
public sealed record TocNode : DocumentNode
{
    /// <summary>Maximum heading level to include (default 3 = H1-H3).</summary>
    public int MaxLevel { get; init; } = 3;

    /// <summary>Optional title displayed above the TOC (e.g. "Table of Contents").</summary>
    public string? Title { get; init; }
}
