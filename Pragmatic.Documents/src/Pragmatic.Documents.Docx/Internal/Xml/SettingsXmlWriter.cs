using System.Xml;

namespace Pragmatic.Documents.Docx.Internal.Xml;

/// <summary>Writes word/settings.xml (document settings).</summary>
internal static class SettingsXmlWriter
{
    internal static void WriteTo(Stream stream, DocxRenderContext ctx)
    {
        using var w = XmlWriter.Create(stream, XmlSettings.Default);
        w.WriteStartDocument(true);
        w.WriteStartElement("w", "settings", Ns.W);

        // Default tab stop (720 twips = 12.7mm)
        w.WriteStartElement("w", "defaultTabStop", Ns.W);
        w.WriteAttributeString("w", "val", Ns.W, "720");
        w.WriteEndElement();

        // Character spacing control
        w.WriteStartElement("w", "characterSpacingControl", Ns.W);
        w.WriteAttributeString("w", "val", Ns.W, "doNotCompress");
        w.WriteEndElement();

        if (ctx.HasToc && ctx.Options.UpdateFieldsOnOpen)
        {
            w.WriteStartElement("w", "updateFields", Ns.W);
            w.WriteAttributeString("w", "val", Ns.W, "true");
            w.WriteEndElement();
        }

        w.WriteEndElement();
    }
}
