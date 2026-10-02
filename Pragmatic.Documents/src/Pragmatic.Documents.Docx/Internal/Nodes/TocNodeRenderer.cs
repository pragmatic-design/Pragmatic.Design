using System.Xml;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Internal.Nodes;

/// <summary>
/// Renders TocNode as a TOC field code with pre-populated heading entries.
/// When UpdateFieldsOnOpen=true, Word updates page numbers on open.
/// When false, page numbers are estimated via heuristic.
/// </summary>
internal static class TocNodeRenderer
{
    internal static void Render(XmlWriter w, TocNode node, DocxRenderContext ctx)
    {
        ctx.HasToc = true;

        // Optional title heading with bookmark (so TOC entry can link to it)
        if (node.Title is not null)
        {
            var bmId = ctx.NextBookmarkId().ToString();

            w.WriteStartElement("w", "p", Ns.W);
            w.WriteStartElement("w", "pPr", Ns.W);
            w.WriteStartElement("w", "pStyle", Ns.W);
            w.WriteAttributeString("w", "val", Ns.W, "Heading1");
            w.WriteEndElement();
            w.WriteEndElement();

            w.WriteStartElement("w", "bookmarkStart", Ns.W);
            w.WriteAttributeString("w", "id", Ns.W, bmId);
            w.WriteAttributeString("w", "name", Ns.W, "_TocTitle");
            w.WriteEndElement();

            w.WriteStartElement("w", "r", Ns.W);
            w.WriteStartElement("w", "t", Ns.W);
            w.WriteString(node.Title);
            w.WriteEndElement();
            w.WriteEndElement();

            w.WriteStartElement("w", "bookmarkEnd", Ns.W);
            w.WriteAttributeString("w", "id", Ns.W, bmId);
            w.WriteEndElement();

            w.WriteEndElement();
        }

        // When UpdateFieldsOnOpen=false, use static TOC (no field code wrapper)
        if (!ctx.Options.UpdateFieldsOnOpen)
        {
            WriteStaticToc(w, node, ctx);
            return;
        }

        // Field-based TOC (Word will update on open)
        WriteFieldToc(w, node, ctx);
    }

    private static void WriteFieldToc(XmlWriter w, TocNode node, DocxRenderContext ctx)
    {
        // TOC field begin
        w.WriteStartElement("w", "p", Ns.W);

        w.WriteStartElement("w", "r", Ns.W);
        w.WriteStartElement("w", "fldChar", Ns.W);
        w.WriteAttributeString("w", "fldCharType", Ns.W, "begin");
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteStartElement("w", "r", Ns.W);
        w.WriteStartElement("w", "instrText", Ns.W);
        w.WriteAttributeString("xml", "space", null, "preserve");
        w.WriteString($" TOC \\o \"1-{node.MaxLevel}\" \\h \\z \\u ");
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteStartElement("w", "r", Ns.W);
        w.WriteStartElement("w", "fldChar", Ns.W);
        w.WriteAttributeString("w", "fldCharType", Ns.W, "separate");
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteEndElement(); // p

        // Pre-populated entries (cached content)
        WriteTocEntries(w, node, ctx);

        // TOC field end
        w.WriteStartElement("w", "p", Ns.W);
        w.WriteStartElement("w", "r", Ns.W);
        w.WriteStartElement("w", "fldChar", Ns.W);
        w.WriteAttributeString("w", "fldCharType", Ns.W, "end");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void WriteStaticToc(XmlWriter w, TocNode node, DocxRenderContext ctx)
    {
        // Pure static paragraphs — no field code, no "update fields" prompt
        WriteTocEntries(w, node, ctx);
    }

    private static void WriteTocEntries(XmlWriter w, TocNode node, DocxRenderContext ctx)
    {
        var estimatedPages = ctx.EstimatedPageNumbers;

        // Include the TOC title itself as the first entry
        if (node.Title is not null)
        {
            // The TOC title page: it's before all headings.
            // Use the page of the first heading minus the TOC's own space, minimum 1.
            var tocTitlePage = ctx.TocTitlePage > 0 ? ctx.TocTitlePage : 1;
            WriteTocEntry(w, 1, "_TocTitle", node.Title, tocTitlePage.ToString());
        }

        var entries = ctx.Headings
            .Select((h, i) => (h.Level, h.BookmarkName, h.Text, Index: i))
            .Where(h => h.Level <= node.MaxLevel)
            .ToList();

        foreach (var (level, bookmarkName, text, index) in entries)
        {
            var pageNum = estimatedPages.GetValueOrDefault(index, 0);
            WriteTocEntry(w, level, bookmarkName, text, pageNum > 0 ? pageNum.ToString() : "");
        }
    }

    private static void WriteTocEntry(XmlWriter w, int level, string bookmarkName, string text, string pageNumber)
    {
        w.WriteStartElement("w", "p", Ns.W);

        // TOC paragraph style + indentation per level
        w.WriteStartElement("w", "pPr", Ns.W);
        w.WriteStartElement("w", "pStyle", Ns.W);
        w.WriteAttributeString("w", "val", Ns.W, $"TOC{Math.Min(level, 3)}");
        w.WriteEndElement();

        // Tab stop for right-aligned page numbers
        w.WriteStartElement("w", "tabs", Ns.W);
        w.WriteStartElement("w", "tab", Ns.W);
        w.WriteAttributeString("w", "val", Ns.W, "right");
        w.WriteAttributeString("w", "leader", Ns.W, "dot");
        w.WriteAttributeString("w", "pos", Ns.W, "9350");
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteEndElement(); // pPr

        // Hyperlink to heading bookmark
        w.WriteStartElement("w", "hyperlink", Ns.W);
        w.WriteAttributeString("w", "anchor", Ns.W, bookmarkName);

        // Heading text
        w.WriteStartElement("w", "r", Ns.W);
        w.WriteStartElement("w", "t", Ns.W);
        w.WriteAttributeString("xml", "space", null, "preserve");
        w.WriteString(text);
        w.WriteEndElement();
        w.WriteEndElement();

        // Tab + page number
        if (pageNumber.Length > 0)
        {
            w.WriteStartElement("w", "r", Ns.W);
            w.WriteStartElement("w", "tab", Ns.W);
            w.WriteEndElement();
            w.WriteEndElement();

            w.WriteStartElement("w", "r", Ns.W);
            w.WriteStartElement("w", "t", Ns.W);
            w.WriteString(pageNumber);
            w.WriteEndElement();
            w.WriteEndElement();
        }

        w.WriteEndElement(); // hyperlink
        w.WriteEndElement(); // p
    }
}
