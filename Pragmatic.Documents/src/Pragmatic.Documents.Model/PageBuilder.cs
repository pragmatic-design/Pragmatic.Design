namespace Pragmatic.Documents.Model;

/// <summary>Fluent builder for <see cref="DocumentPage"/>.</summary>
public sealed class PageBuilder
{
    private readonly List<DocumentNode> _content = [];
    private List<DocumentNode>? _header;
    private List<DocumentNode>? _footer;
    private PageSize? _pageSize;
    private PageOrientation? _orientation;
    private Margins? _margins;
    private bool _differentFirstPage;
    private List<DocumentNode>? _firstPageHeader;
    private List<DocumentNode>? _firstPageFooter;

    public PageBuilder Header(params DocumentNode[] nodes)
    {
        _header = [..nodes];
        return this;
    }

    public PageBuilder Footer(params DocumentNode[] nodes)
    {
        _footer = [..nodes];
        return this;
    }

    /// <summary>Override the page size for this page only (falls back to the document size when unset).</summary>
    public PageBuilder Size(PageSize size) { _pageSize = size; return this; }

    /// <summary>Override this page's orientation to landscape (falls back to the document orientation when unset).</summary>
    public PageBuilder Landscape() { _orientation = PageOrientation.Landscape; return this; }

    /// <summary>Override the margins for this page only (falls back to the document margins when unset).</summary>
    public PageBuilder WithMargins(Margins margins) { _margins = margins; return this; }

    /// <summary>Mark this section as having a different first page — e.g. a blank first-page header/footer — even without distinct first-page content.</summary>
    public PageBuilder DifferentFirstPage(bool value = true) { _differentFirstPage = value; return this; }

    /// <summary>Give the first page of this section a distinct header. Automatically enables <see cref="DocumentPage.DifferentFirstPage"/>.</summary>
    public PageBuilder FirstPageHeader(params DocumentNode[] nodes)
    {
        _firstPageHeader = [..nodes];
        _differentFirstPage = true;
        return this;
    }

    /// <summary>Give the first page of this section a distinct footer. Automatically enables <see cref="DocumentPage.DifferentFirstPage"/>.</summary>
    public PageBuilder FirstPageFooter(params DocumentNode[] nodes)
    {
        _firstPageFooter = [..nodes];
        _differentFirstPage = true;
        return this;
    }

    public PageBuilder Add(DocumentNode node) { _content.Add(node); return this; }
    public PageBuilder Text(string content, NodeStyle? style = null) => Add(new TextNode { Content = content, Style = style });
    public PageBuilder Heading(string content, int level = 1) => Add(new HeadingNode { Content = content, Level = level });
    public PageBuilder Image(string source, double? width = null, double? height = null, string? alt = null) => Add(new ImageNode { Source = source, Width = width, Height = height, Alt = alt });
    public PageBuilder HorizontalRule() => Add(new HorizontalRuleNode());
    public PageBuilder Spacer(double height = 10) => Add(new SpacerNode { Height = height });
    public PageBuilder PageBreak() => Add(new PageBreakNode());
    public PageBuilder Barcode(string value, BarcodeType type = BarcodeType.QrCode) => Add(new BarcodeNode { Value = value, Type = type });
    public PageBuilder Hyperlink(string href, string text) => Add(new HyperlinkNode { Href = href, Children = [new TextNode { Content = text }] });
    public PageBuilder Toc(int maxLevel = 3, string? title = null) => Add(new TocNode { MaxLevel = maxLevel, Title = title });
    public PageBuilder Footnote(string content) => Add(new FootnoteNode { Content = content });
    public PageBuilder Field(FieldType fieldType, string? format = null) => Add(new FieldNode { FieldType = fieldType, Format = format });
    public PageBuilder Bookmark(string name, params DocumentNode[] children) => Add(new BookmarkNode { Name = name, Children = children });

    /// <summary>Add a paragraph with inline children.</summary>
    public PageBuilder Paragraph(params DocumentNode[] children)
        => Add(new ParagraphNode { Children = children });

    /// <summary>Add a table using a table builder.</summary>
    public PageBuilder Table(Action<TableBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new TableBuilder();
        configure(builder);
        _content.Add(builder.Build());
        return this;
    }

    internal DocumentPage Build() => new()
    {
        PageSize = _pageSize,
        Orientation = _orientation,
        Margins = _margins,
        // Defensive copy of content (mutated via Add); header/footer lists are already
        // freshly built per call, but copy them too for a uniform no-alias contract.
        Content = [.._content],
        Header = _header is null ? null : [.._header],
        Footer = _footer is null ? null : [.._footer],
        DifferentFirstPage = _differentFirstPage,
        FirstPageHeader = _firstPageHeader is null ? null : [.._firstPageHeader],
        FirstPageFooter = _firstPageFooter is null ? null : [.._firstPageFooter]
    };
}
