using System.IO.Compression;
using System.Xml;
using Pragmatic.Documents.Docx.Internal.Xml;
using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Internal;

/// <summary>
/// Assembles all OOXML parts into a ZIP archive (the .docx file).
/// </summary>
internal static class DocxPackageWriter
{
    internal static byte[] Build(DocumentModel model, DocxResources? resources, DocxRenderOptions options)
    {
        using var ms = new MemoryStream();
        BuildTo(ms, model, resources, options);
        return ms.ToArray();
    }

    /// <summary>
    /// Assembles the .docx ZIP directly into <paramref name="output"/> without first
    /// materializing the whole document as a byte[] — avoids double-buffering for large docs.
    /// </summary>
    internal static void BuildTo(Stream output, DocumentModel model, DocxResources? resources, DocxRenderOptions options)
    {
        var ctx = new DocxRenderContext { Resources = resources, Options = options };

        // Pre-register fixed relationships for document.xml.rels
        var stylesRelId = ctx.DocumentRels.Add(Ns.RelStyles, "styles.xml");
        var numberingRelId = ctx.DocumentRels.Add(Ns.RelNumbering, "numbering.xml");
        var settingsRelId = ctx.DocumentRels.Add(Ns.RelSettings, "settings.xml");
        var fontTableRelId = ctx.DocumentRels.Add(Ns.RelFontTable, "fontTable.xml");
        var themeRelId = ctx.DocumentRels.Add(Ns.RelTheme, "theme/theme1.xml");

        // Load template if provided (template parts override generated ones)
        var template = options.Template is not null ? DocxTemplateLoader.Load(options.Template) : null;
        var theme = options.EffectiveTheme;

        // Render the document body (this populates ctx with footnotes, images, headers, etc.)
        var documentXml = RenderPart(s => DocumentXmlWriter.WriteTo(s, model, ctx));

        // Styles: template > generated from theme
        var stylesXml = template?.StylesXml ?? RenderPart(s => StylesXmlWriter.WriteTo(s, theme));

        // Numbering: template > generated
        var numberingXml = template?.NumberingXml ?? RenderPart(s => NumberingXmlWriter.WriteTo(s, ctx));

        // Render footnotes (if any were collected)
        byte[]? footnotesXml = null;
        string? footnotesRelId = null;
        if (ctx.Footnotes.Count > 0)
        {
            footnotesRelId = ctx.DocumentRels.Add(Ns.RelFootnotes, "footnotes.xml");
            footnotesXml = RenderPart(s => FootnotesXmlWriter.WriteTo(s, ctx));
        }

        // Build the ZIP straight into the caller's stream (no intermediate byte[]).
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            // Root relationships
            WriteRootRels(zip);

            // Document relationships
            WriteEntry(zip, "word/_rels/document.xml.rels", s => ctx.DocumentRels.WriteTo(s));

            // Core XML parts
            WriteEntry(zip, "word/document.xml", documentXml);
            WriteEntry(zip, "word/styles.xml", stylesXml);
            WriteEntry(zip, "word/numbering.xml", numberingXml);
            WriteEntry(zip, "word/settings.xml", s => SettingsXmlWriter.WriteTo(s, ctx));
            // Font table: template > generated
            if (template?.FontTableXml is not null)
                WriteEntry(zip, "word/fontTable.xml", template.FontTableXml);
            else
                WriteEntry(zip, "word/fontTable.xml", s => FontTableXmlWriter.WriteTo(s, theme));

            // Theme: template > generated
            if (template?.ThemeXml is not null)
                WriteEntry(zip, "word/theme/theme1.xml", template.ThemeXml);
            else
                WriteEntry(zip, "word/theme/theme1.xml", s => ThemeXmlWriter.WriteTo(s, theme));

            // Optional: footnotes
            if (footnotesXml is not null)
                WriteEntry(zip, "word/footnotes.xml", footnotesXml);

            // Header/footer parts + their part-scoped rels
            foreach (var (partName, _, xml, partRels) in ctx.HeaderFooterParts)
            {
                WriteEntry(zip, $"word/{partName}", xml);
                if (partRels is not null)
                    WriteEntry(zip, $"word/_rels/{partName}.rels", s => partRels.WriteTo(s));
            }

            // Media (images)
            foreach (var (fileName, data, _) in ctx.MediaEntries)
                WriteEntry(zip, $"word/media/{fileName}", data);

            // Document properties
            WriteEntry(zip, "docProps/core.xml", s => CorePropsXmlWriter.WriteTo(s, model));
            WriteEntry(zip, "docProps/app.xml", s => AppPropsXmlWriter.WriteTo(s));

            // Content types (must be written last — needs full inventory)
            WriteContentTypes(zip, ctx, footnotesXml is not null);
        }
    }

    private static void WriteRootRels(ZipArchive zip)
    {
        var rels = new DocxRelationships();
        rels.Add(Ns.RelDocument, "word/document.xml");
        rels.Add(Ns.RelCoreProps, "docProps/core.xml");
        rels.Add(Ns.RelAppProps, "docProps/app.xml");

        WriteEntry(zip, "_rels/.rels", s => rels.WriteTo(s));
    }

    private static void WriteContentTypes(ZipArchive zip, DocxRenderContext ctx, bool hasFootnotes)
    {
        WriteEntry(zip, "[Content_Types].xml", stream =>
        {
            using var w = XmlWriter.Create(stream, XmlSettings.Default);
            w.WriteStartDocument(true);
            w.WriteStartElement("Types", Ns.CT);

            // Default extensions
            WriteDefault(w, "rels", "application/vnd.openxmlformats-package.relationships+xml");
            WriteDefault(w, "xml", "application/xml");

            // Image extensions
            var imageExts = ctx.MediaEntries.Select(e => (Path.GetExtension(e.FileName).TrimStart('.'), e.ContentType)).Distinct();
            foreach (var (ext, ct) in imageExts)
                WriteDefault(w, ext, ct);

            // Part overrides
            WriteOverride(w, "/word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml");
            WriteOverride(w, "/word/styles.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml");
            WriteOverride(w, "/word/numbering.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml");
            WriteOverride(w, "/word/settings.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml");
            WriteOverride(w, "/word/fontTable.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.fontTable+xml");
            WriteOverride(w, "/word/theme/theme1.xml", "application/vnd.openxmlformats-officedocument.theme+xml");

            if (hasFootnotes)
                WriteOverride(w, "/word/footnotes.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml");

            foreach (var (partName, _, _, _) in ctx.HeaderFooterParts)
            {
                var contentType = partName.StartsWith("header")
                    ? "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml"
                    : "application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml";
                WriteOverride(w, $"/word/{partName}", contentType);
            }

            WriteOverride(w, "/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml");
            WriteOverride(w, "/docProps/app.xml", "application/vnd.openxmlformats-officedocument.extended-properties+xml");

            w.WriteEndElement();
        });
    }

    private static void WriteDefault(XmlWriter w, string extension, string contentType)
    {
        w.WriteStartElement("Default", Ns.CT);
        w.WriteAttributeString("Extension", extension);
        w.WriteAttributeString("ContentType", contentType);
        w.WriteEndElement();
    }

    private static void WriteOverride(XmlWriter w, string partName, string contentType)
    {
        w.WriteStartElement("Override", Ns.CT);
        w.WriteAttributeString("PartName", partName);
        w.WriteAttributeString("ContentType", contentType);
        w.WriteEndElement();
    }

    private static byte[] RenderPart(Action<Stream> writer)
    {
        using var ms = new MemoryStream();
        writer(ms);
        return ms.ToArray();
    }

    private static void WriteEntry(ZipArchive zip, string path, byte[] data)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Fastest);
        using var s = entry.Open();
        s.Write(data, 0, data.Length);
    }

    private static void WriteEntry(ZipArchive zip, string path, Action<Stream> writer)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Fastest);
        using var s = entry.Open();
        writer(s);
    }
}
