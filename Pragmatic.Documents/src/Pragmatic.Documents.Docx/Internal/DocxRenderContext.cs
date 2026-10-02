using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Internal;

/// <summary>
/// Mutable state accumulated during DOCX rendering.
/// Collects footnotes, images, headings, and relationships for deferred writing.
/// </summary>
internal sealed class DocxRenderContext
{
    /// <summary>Document-level relationships (word/_rels/document.xml.rels).</summary>
    internal DocxRelationships DocumentRels { get; } = new();

    private DocxRelationships? _activeRels;

    /// <summary>
    /// Active relationship target. Defaults to DocumentRels; set to part-scoped rels for header/footer.
    /// </summary>
    internal DocxRelationships ActiveRels
    {
        get => _activeRels ?? DocumentRels;
        set => _activeRels = value;
    }

    internal void ResetActiveRels() => _activeRels = null;

    /// <summary>Footnotes collected during body rendering (id → body text).</summary>
    internal List<string> Footnotes { get; } = [];

    /// <summary>Headings encountered (for TOC bookmark generation).</summary>
    internal List<(int Level, string BookmarkName, string Text)> Headings { get; } = [];

    /// <summary>Images to embed in word/media/ (filename → bytes).</summary>
    internal List<(string FileName, byte[] Data, string ContentType)> MediaEntries { get; } = [];

    /// <summary>Numbering definitions collected for numbering.xml.</summary>
    internal List<NumberingDef> NumberingDefs { get; } = [];

    /// <summary>Header/footer XML parts to write, with optional part-scoped relationships.</summary>
    internal List<(string PartName, string RelId, byte[] Xml, DocxRelationships? PartRels)> HeaderFooterParts { get; } = [];

    /// <summary>External resources (name → bytes).</summary>
    internal DocxResources? Resources { get; init; }

    /// <summary>Render options.</summary>
    internal DocxRenderOptions Options { get; init; } = DocxRenderOptions.Default;

    /// <summary>
    /// Timestamp for DATE/TIME field placeholders, captured once per render so every field in a
    /// document is consistent. Sourced from <see cref="DocxRenderOptions.RenderTimestamp"/> when
    /// provided (deterministic), otherwise the wall clock at first access.
    /// </summary>
    internal DateTimeOffset RenderTimestamp => _renderTimestamp ??= Options.RenderTimestamp ?? DateTimeOffset.Now;
    private DateTimeOffset? _renderTimestamp;

    /// <summary>Whether the document contains at least one TocNode.</summary>
    internal bool HasToc { get; set; }

    /// <summary>Estimated page numbers per heading index (set by DocumentXmlWriter pre-scan).</summary>
    internal Dictionary<int, int> EstimatedPageNumbers { get; set; } = [];

    /// <summary>Estimated page where the TOC title appears.</summary>
    internal int TocTitlePage { get; set; } = 1;

    private int _nextBookmarkId;
    private int _nextNumberingId;
    private int _nextHeadingRenderIndex;
    private int _nextImageId = 1;

    /// <summary>Get the next unique bookmark ID.</summary>
    internal int NextBookmarkId() => _nextBookmarkId++;

    /// <summary>Get the next unique image ID for docPr/cNvPr.</summary>
    internal int NextImageId() => _nextImageId++;

    /// <summary>Get the bookmark name for the next heading being rendered (matches pre-scan order).</summary>
    internal string? GetNextHeadingBookmark()
    {
        if (_nextHeadingRenderIndex < Headings.Count)
            return Headings[_nextHeadingRenderIndex++].BookmarkName;
        return null;
    }

    /// <summary>Get the next unique numbering ID.</summary>
    internal int NextNumberingId() => ++_nextNumberingId;

    /// <summary>Add a footnote and return its 1-based ID.</summary>
    internal int AddFootnote(string content)
    {
        Footnotes.Add(content);
        return Footnotes.Count; // 1-based (0 = separator, 1 = continuationSeparator are implicit)
    }

    /// <summary>Register an image and return its rId + filename.</summary>
    internal (string RelId, string FileName) AddImage(string name, byte[] data)
    {
        var ext = DetectImageExtension(data);
        var fileName = $"image{MediaEntries.Count + 1}{ext}";
        var contentType = ext switch
        {
            ".png" => "image/png",
            ".jpg" => "image/jpeg",
            ".gif" => "image/gif",
            ".bmp" => "image/bmp",
            _ => "image/png"
        };

        MediaEntries.Add((fileName, data, contentType));
        var relId = ActiveRels.Add(Ns.RelImage, $"media/{fileName}");
        return (relId, fileName);
    }

    private static string DetectImageExtension(byte[] data)
    {
        if (data.Length < 4) return ".png";

        // PNG: 89 50 4E 47
        if (data[0] == 0x89 && data[1] == 0x50) return ".png";
        // JPEG: FF D8
        if (data[0] == 0xFF && data[1] == 0xD8) return ".jpg";
        // GIF: 47 49 46
        if (data[0] == 0x47 && data[1] == 0x49 && data[2] == 0x46) return ".gif";
        // BMP: 42 4D
        if (data[0] == 0x42 && data[1] == 0x4D) return ".bmp";

        return ".png";
    }
}

