namespace Pragmatic.Documents.Model;

/// <summary>
/// A page in the document. Contains header, footer, and content nodes.
/// </summary>
public sealed record DocumentPage
{
    /// <summary>Optional page-level override for page size.</summary>
    public PageSize? PageSize { get; init; }

    /// <summary>Optional page-level override for orientation.</summary>
    public PageOrientation? Orientation { get; init; }

    /// <summary>Optional page-level override for margins.</summary>
    public Margins? Margins { get; init; }

    /// <summary>Page header nodes (rendered at top of each page).</summary>
    public IReadOnlyList<DocumentNode>? Header { get; init; }

    /// <summary>Page footer nodes (rendered at bottom of each page).</summary>
    public IReadOnlyList<DocumentNode>? Footer { get; init; }

    /// <summary>Page content nodes.</summary>
    public IReadOnlyList<DocumentNode> Content { get; init; } = [];

    /// <summary>When true, the first page of this section has a different header/footer.</summary>
    public bool DifferentFirstPage { get; init; }

    /// <summary>Header nodes for the first page only (when DifferentFirstPage is true).</summary>
    public IReadOnlyList<DocumentNode>? FirstPageHeader { get; init; }

    /// <summary>Footer nodes for the first page only (when DifferentFirstPage is true).</summary>
    public IReadOnlyList<DocumentNode>? FirstPageFooter { get; init; }
}
