namespace Pragmatic.Documents.Model;

/// <summary>
/// Fluent builder for constructing <see cref="DocumentModel"/> instances.
/// </summary>
public sealed class DocumentBuilder
{
    private string? _title;
    private string? _author;
    private string? _language;
    private PageSize _pageSize = PageSize.A4;
    private PageOrientation _orientation = PageOrientation.Portrait;
    private Margins _margins = Margins.Default;
    private string? _subject;
    private string? _keywords;
    private DateTimeOffset? _createdDate;
    private readonly List<DocumentPage> _pages = [];
    private Dictionary<string, string>? _metadata;

    public DocumentBuilder Title(string title) { _title = title; return this; }
    public DocumentBuilder Author(string author) { _author = author; return this; }
    public DocumentBuilder Language(string language) { _language = language; return this; }
    public DocumentBuilder Size(PageSize size) { _pageSize = size; return this; }
    public DocumentBuilder Landscape() { _orientation = PageOrientation.Landscape; return this; }
    public DocumentBuilder WithMargins(Margins margins) { _margins = margins; return this; }

    public DocumentBuilder Subject(string subject) { _subject = subject; return this; }
    public DocumentBuilder Keywords(string keywords) { _keywords = keywords; return this; }

    /// <summary>Set the document creation timestamp (written to OOXML core properties; defaults to now when unset).</summary>
    public DocumentBuilder CreatedDate(DateTimeOffset createdDate) { _createdDate = createdDate; return this; }

    public DocumentBuilder Meta(string key, string value)
    {
        _metadata ??= [];
        _metadata[key] = value;
        return this;
    }

    /// <summary>Add a page using a page builder.</summary>
    public DocumentBuilder Page(Action<PageBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new PageBuilder();
        configure(builder);
        _pages.Add(builder.Build());
        return this;
    }

    /// <summary>Add a pre-built page.</summary>
    public DocumentBuilder Page(DocumentPage page)
    {
        _pages.Add(page);
        return this;
    }

    public DocumentModel Build() => new()
    {
        Title = _title,
        Author = _author,
        Language = _language,
        Subject = _subject,
        Keywords = _keywords,
        PageSize = _pageSize,
        Orientation = _orientation,
        Margins = _margins,
        CreatedDate = _createdDate,
        // Defensive copies: the returned model must not alias the builder's mutable state.
        Pages = [.._pages],
        Metadata = _metadata is null ? null : new Dictionary<string, string>(_metadata)
    };
}
