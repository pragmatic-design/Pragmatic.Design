using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Templates;

/// <summary>
/// Root document template. Parallel to <see cref="DocumentModel"/> but with expression support.
/// </summary>
public sealed record DocumentTemplate
{
    /// <summary>Title — can contain <c>{{expressions}}</c>.</summary>
    public string? Title { get; init; }

    /// <summary>Author — can contain <c>{{expressions}}</c>.</summary>
    public string? Author { get; init; }

    public string? Language { get; init; }
    public PageSize PageSize { get; init; } = PageSize.A4;
    public PageOrientation Orientation { get; init; } = PageOrientation.Portrait;
    public Margins Margins { get; init; } = Margins.Default;

    /// <summary>Pages of the document.</summary>
    public IReadOnlyList<DocumentPageTemplate> Pages { get; init; } = [];

    /// <summary>
    /// Data source path for page repetition (batch report).
    /// When set, each item in the collection produces one page using the first page as template.
    /// </summary>
    public string? PageDataSource { get; init; }

    /// <summary>Item variable name when using PageDataSource. Default: <c>"item"</c>.</summary>
    public string PageItemName { get; init; } = "item";
}
