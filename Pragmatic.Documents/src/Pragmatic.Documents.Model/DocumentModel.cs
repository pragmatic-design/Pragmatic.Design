namespace Pragmatic.Documents.Model;

/// <summary>
/// Root of a document. Contains metadata and a list of pages.
/// This is the JSON contract between producers (Razor TagHelper, frontend editor, API)
/// and renderers (PDF, DOCX, XLSX, CSV).
/// </summary>
public sealed record DocumentModel
{
    /// <summary>Document title (used in PDF metadata, DOCX title, etc.).</summary>
    public string? Title { get; init; }

    /// <summary>Document author.</summary>
    public string? Author { get; init; }

    /// <summary>Document language (BCP-47, e.g. "it-IT").</summary>
    public string? Language { get; init; }

    /// <summary>Page size. Default A4.</summary>
    public PageSize PageSize { get; init; } = PageSize.A4;

    /// <summary>Page orientation.</summary>
    public PageOrientation Orientation { get; init; } = PageOrientation.Portrait;

    /// <summary>Page margins in mm.</summary>
    public Margins Margins { get; init; } = Margins.Default;

    /// <summary>The document pages.</summary>
    public IReadOnlyList<DocumentPage> Pages { get; init; } = [];

    /// <summary>Document subject.</summary>
    public string? Subject { get; init; }

    /// <summary>Document keywords (comma-separated).</summary>
    public string? Keywords { get; init; }

    /// <summary>Document creation date.</summary>
    public DateTimeOffset? CreatedDate { get; init; }

    /// <summary>Custom metadata key-value pairs.</summary>
    public IReadOnlyDictionary<string, string>? Metadata { get; init; }
}
