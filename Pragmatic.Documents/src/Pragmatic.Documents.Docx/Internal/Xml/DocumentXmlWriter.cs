using System.Xml;
using Pragmatic.Documents.Docx.Internal.Nodes;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Internal.Xml;

/// <summary>Writes word/document.xml — the main body content.</summary>
internal static class DocumentXmlWriter
{
    internal static void WriteTo(Stream stream, DocumentModel model, DocxRenderContext ctx)
    {
        // Pre-scan: collect all headings for TOC pre-population + estimate page numbers
        PreScanHeadings(model, ctx);
        var estimation = PageEstimator.EstimatePageNumbers(model);
        ctx.EstimatedPageNumbers = estimation.PageNumbers;
        ctx.TocTitlePage = estimation.TocTitlePage;

        using var w = XmlWriter.Create(stream, XmlSettings.Default);
        w.WriteStartDocument(true);
        w.WriteStartElement("w", "document", Ns.W);
        w.WriteAttributeString("xmlns", "r", null, Ns.R);
        w.WriteAttributeString("xmlns", "wp", null, Ns.WP);
        w.WriteAttributeString("xmlns", "a", null, Ns.A);
        w.WriteAttributeString("xmlns", "pic", null, Ns.PIC);

        w.WriteStartElement("w", "body", Ns.W);

        for (var i = 0; i < model.Pages.Count; i++)
        {
            var page = model.Pages[i];
            var isLast = i == model.Pages.Count - 1;

            // Register headers/footers for this section
            RegisterHeaderFooter(page, ctx, i);

            // Render page content
            NodeRenderer.RenderAll(w, page.Content, ctx);

            if (!isLast)
            {
                // Section break (as last paragraph's pPr in the section)
                w.WriteStartElement("w", "p", Ns.W);
                w.WriteStartElement("w", "pPr", Ns.W);
                WriteSectionProperties(w, model, page, ctx, i, "nextPage");
                w.WriteEndElement(); // pPr
                w.WriteEndElement(); // p
            }
        }

        // Final section properties (required, no break type)
        if (model.Pages.Count > 0)
        {
            var lastPage = model.Pages[^1];
            WriteSectionProperties(w, model, lastPage, ctx, model.Pages.Count - 1);
        }
        else
        {
            // Empty document: write default section properties
            WriteDefaultSectionProperties(w, model);
        }

        w.WriteEndElement(); // body
        w.WriteEndElement(); // document
    }

    private static void WriteSectionProperties(
        XmlWriter w, DocumentModel model, DocumentPage page, DocxRenderContext ctx, int pageIndex, string? breakType = null)
    {
        var pageSize = page.PageSize ?? model.PageSize;
        var orientation = page.Orientation ?? model.Orientation;
        var margins = page.Margins ?? model.Margins;
        var (pgW, pgH) = Units.PageSizeToTwips(pageSize, orientation);

        w.WriteStartElement("w", "sectPr", Ns.W);

        // Header/footer references
        foreach (var (partName, relId, _, _) in ctx.HeaderFooterParts)
        {
            // Exact section match: a part name is "header{n}.xml" or "header{n}first.xml"
            // (likewise footer). Match the numeric token precisely so header1 != header10.
            if (PartSectionIndex(partName) != pageIndex + 1) continue;

            var type = partName.Contains("first") ? "first" : "default";
            var element = partName.StartsWith("header") ? "headerReference" : "footerReference";

            w.WriteStartElement("w", element, Ns.W);
            w.WriteAttributeString("w", "type", Ns.W, type);
            w.WriteAttributeString("r", "id", Ns.R, relId);
            w.WriteEndElement();
        }

        if (breakType is not null)
        {
            w.WriteStartElement("w", "type", Ns.W);
            w.WriteAttributeString("w", "val", Ns.W, breakType);
            w.WriteEndElement();
        }

        // Page size
        w.WriteStartElement("w", "pgSz", Ns.W);
        w.WriteAttributeString("w", "w", Ns.W, pgW.ToString());
        w.WriteAttributeString("w", "h", Ns.W, pgH.ToString());
        if (orientation == PageOrientation.Landscape)
            w.WriteAttributeString("w", "orient", Ns.W, "landscape");
        w.WriteEndElement();

        // Margins
        w.WriteStartElement("w", "pgMar", Ns.W);
        w.WriteAttributeString("w", "top", Ns.W, Units.MmToTwips(margins.Top).ToString());
        w.WriteAttributeString("w", "right", Ns.W, Units.MmToTwips(margins.Right).ToString());
        w.WriteAttributeString("w", "bottom", Ns.W, Units.MmToTwips(margins.Bottom).ToString());
        w.WriteAttributeString("w", "left", Ns.W, Units.MmToTwips(margins.Left).ToString());
        w.WriteAttributeString("w", "header", Ns.W, "708");
        w.WriteAttributeString("w", "footer", Ns.W, "708");
        w.WriteEndElement();

        // Different first page
        if (page.DifferentFirstPage)
        {
            w.WriteStartElement("w", "titlePg", Ns.W);
            w.WriteEndElement();
        }

        w.WriteEndElement(); // sectPr
    }

    /// <summary>
    /// Parses the 1-based section index out of a header/footer part name
    /// ("header{n}.xml" or "header{n}first.xml"). Returns -1 if no index is found.
    /// </summary>
    private static int PartSectionIndex(string partName)
    {
        // Skip the literal prefix ("header" / "footer"), then read the leading digits.
        var i = 0;
        while (i < partName.Length && !char.IsDigit(partName[i])) i++;
        var start = i;
        while (i < partName.Length && char.IsDigit(partName[i])) i++;
        if (i == start) return -1;
        return int.TryParse(partName.AsSpan(start, i - start), out var n) ? n : -1;
    }

    private static void WriteDefaultSectionProperties(XmlWriter w, DocumentModel model)
    {
        var (pgW, pgH) = Units.PageSizeToTwips(model.PageSize, model.Orientation);

        w.WriteStartElement("w", "sectPr", Ns.W);
        w.WriteStartElement("w", "pgSz", Ns.W);
        w.WriteAttributeString("w", "w", Ns.W, pgW.ToString());
        w.WriteAttributeString("w", "h", Ns.W, pgH.ToString());
        w.WriteEndElement();
        w.WriteStartElement("w", "pgMar", Ns.W);
        w.WriteAttributeString("w", "top", Ns.W, Units.MmToTwips(model.Margins.Top).ToString());
        w.WriteAttributeString("w", "right", Ns.W, Units.MmToTwips(model.Margins.Right).ToString());
        w.WriteAttributeString("w", "bottom", Ns.W, Units.MmToTwips(model.Margins.Bottom).ToString());
        w.WriteAttributeString("w", "left", Ns.W, Units.MmToTwips(model.Margins.Left).ToString());
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static void RegisterHeaderFooter(DocumentPage page, DocxRenderContext ctx, int pageIndex)
    {
        if (page.Header is { Count: > 0 })
            RegisterPart(ctx, page.Header, $"header{pageIndex + 1}.xml", Ns.RelHeader, "w", "hdr");

        if (page.Footer is { Count: > 0 })
            RegisterPart(ctx, page.Footer, $"footer{pageIndex + 1}.xml", Ns.RelFooter, "w", "ftr");

        if (page.DifferentFirstPage)
        {
            if (page.FirstPageHeader is { Count: > 0 })
                RegisterPart(ctx, page.FirstPageHeader, $"header{pageIndex + 1}first.xml", Ns.RelHeader, "w", "hdr");

            if (page.FirstPageFooter is { Count: > 0 })
                RegisterPart(ctx, page.FirstPageFooter, $"footer{pageIndex + 1}first.xml", Ns.RelFooter, "w", "ftr");
        }
    }

    private static void RegisterPart(
        DocxRenderContext ctx, IReadOnlyList<DocumentNode> nodes, string partName, string relType, string prefix, string rootElement)
    {
        var partRels = new DocxRelationships();
        ctx.ActiveRels = partRels;

        using var ms = new MemoryStream();
        using (var w = XmlWriter.Create(ms, XmlSettings.Default))
        {
            w.WriteStartDocument(true);
            w.WriteStartElement(prefix, rootElement, Ns.W);
            w.WriteAttributeString("xmlns", "r", null, Ns.R);

            foreach (var node in nodes)
                NodeRenderer.Render(w, node, ctx);

            w.WriteEndElement();
        }

        ctx.ResetActiveRels();

        var relId = ctx.DocumentRels.Add(relType, partName);
        ctx.HeaderFooterParts.Add((partName, relId, ms.ToArray(), partRels));
    }

    /// <summary>
    /// Pre-scan all pages to collect heading nodes. This allows TOC to be
    /// pre-populated with heading text and bookmark links before rendering.
    /// </summary>
    private static void PreScanHeadings(DocumentModel model, DocxRenderContext ctx)
    {
        foreach (var page in model.Pages)
            ScanNodes(page.Content, ctx);
    }

    private static void ScanNodes(IEnumerable<DocumentNode> nodes, DocxRenderContext ctx)
    {
        // Walk in render order across every container (tables, lists, bookmarks, ...) so the heading
        // counter stays in lockstep with render-time consumption.
        HeadingScanner.Walk(nodes, heading =>
            ctx.Headings.Add((heading.Level, $"_Heading_{ctx.Headings.Count}", heading.Content)));
    }
}
