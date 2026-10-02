using System.Xml;

namespace Pragmatic.Documents.Docx.Internal.Xml;

/// <summary>Writes word/fontTable.xml with font declarations from the theme.</summary>
internal static class FontTableXmlWriter
{
    internal static void WriteTo(Stream stream, DocxTheme theme)
    {
        using var w = XmlWriter.Create(stream, XmlSettings.Default);
        w.WriteStartDocument(true);
        w.WriteStartElement("w", "fonts", Ns.W);

        // Always include the theme fonts
        var fonts = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            theme.BodyFont,
            theme.HeadingFont
        };

        // Add common fallback fonts
        fonts.Add("Times New Roman");
        fonts.Add("Courier New");

        foreach (var font in fonts)
        {
            var family = font switch
            {
                "Times New Roman" or "Georgia" or "Garamond" or "Book Antiqua" => "roman",
                "Courier New" or "Consolas" or "Lucida Console" => "modern",
                _ => "swiss"
            };
            WriteFont(w, font, family);
        }

        w.WriteEndElement();
    }

    private static void WriteFont(XmlWriter w, string name, string family)
    {
        w.WriteStartElement("w", "font", Ns.W);
        w.WriteAttributeString("w", "name", Ns.W, name);
        w.WriteStartElement("w", "family", Ns.W);
        w.WriteAttributeString("w", "val", Ns.W, family);
        w.WriteEndElement();
        w.WriteEndElement();
    }
}
