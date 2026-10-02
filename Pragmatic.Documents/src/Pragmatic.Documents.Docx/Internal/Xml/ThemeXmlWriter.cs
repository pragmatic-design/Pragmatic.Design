using System.Xml;

namespace Pragmatic.Documents.Docx.Internal.Xml;

/// <summary>Writes word/theme/theme1.xml with OOXML color and font scheme.</summary>
internal static class ThemeXmlWriter
{
    internal static void WriteTo(Stream stream, DocxTheme theme)
    {
        using var w = XmlWriter.Create(stream, XmlSettings.Default);
        w.WriteStartDocument(true);
        w.WriteStartElement("a", "theme", Ns.A);
        w.WriteAttributeString("name", "Pragmatic");

        w.WriteStartElement("a", "themeElements", Ns.A);

        WriteColorScheme(w, theme);
        WriteFontScheme(w, theme);
        WriteFormatScheme(w);

        w.WriteEndElement(); // themeElements

        // Empty object defaults
        w.WriteStartElement("a", "objectDefaults", Ns.A); w.WriteEndElement();
        w.WriteStartElement("a", "extraClrSchemeLst", Ns.A); w.WriteEndElement();

        w.WriteEndElement(); // theme
    }

    private static void WriteColorScheme(XmlWriter w, DocxTheme theme)
    {
        w.WriteStartElement("a", "clrScheme", Ns.A);
        w.WriteAttributeString("name", "Pragmatic");

        WriteSrgbColor(w, "dk1", theme.TextColor);
        WriteSrgbColor(w, "lt1", theme.BackgroundColor);
        WriteSrgbColor(w, "dk2", theme.SecondaryColor);
        WriteSrgbColor(w, "lt2", "E7E6E6");
        WriteSrgbColor(w, "accent1", theme.AccentColor);
        WriteSrgbColor(w, "accent2", DarkenColor(theme.AccentColor, 0.15));
        WriteSrgbColor(w, "accent3", LightenColor(theme.AccentColor, 0.2));
        WriteSrgbColor(w, "accent4", DarkenColor(theme.AccentColor, 0.3));
        WriteSrgbColor(w, "accent5", LightenColor(theme.AccentColor, 0.4));
        WriteSrgbColor(w, "accent6", DarkenColor(theme.AccentColor, 0.45));
        WriteSrgbColor(w, "hlink", theme.HyperlinkColor);
        WriteSrgbColor(w, "folHlink", "954F72");

        w.WriteEndElement(); // clrScheme
    }

    private static void WriteFontScheme(XmlWriter w, DocxTheme theme)
    {
        w.WriteStartElement("a", "fontScheme", Ns.A);
        w.WriteAttributeString("name", "Pragmatic");

        // Major (headings)
        w.WriteStartElement("a", "majorFont", Ns.A);
        w.WriteStartElement("a", "latin", Ns.A);
        w.WriteAttributeString("typeface", theme.HeadingFont);
        w.WriteEndElement();
        w.WriteStartElement("a", "ea", Ns.A);
        w.WriteAttributeString("typeface", "");
        w.WriteEndElement();
        w.WriteStartElement("a", "cs", Ns.A);
        w.WriteAttributeString("typeface", "");
        w.WriteEndElement();
        w.WriteEndElement(); // majorFont

        // Minor (body)
        w.WriteStartElement("a", "minorFont", Ns.A);
        w.WriteStartElement("a", "latin", Ns.A);
        w.WriteAttributeString("typeface", theme.BodyFont);
        w.WriteEndElement();
        w.WriteStartElement("a", "ea", Ns.A);
        w.WriteAttributeString("typeface", "");
        w.WriteEndElement();
        w.WriteStartElement("a", "cs", Ns.A);
        w.WriteAttributeString("typeface", "");
        w.WriteEndElement();
        w.WriteEndElement(); // minorFont

        w.WriteEndElement(); // fontScheme
    }

    private static void WriteFormatScheme(XmlWriter w)
    {
        // Minimal format scheme (required by OOXML, but we keep it simple)
        w.WriteStartElement("a", "fmtScheme", Ns.A);
        w.WriteAttributeString("name", "Pragmatic");

        // Fill style list (3 required)
        w.WriteStartElement("a", "fillStyleLst", Ns.A);
        for (var i = 0; i < 3; i++)
        {
            w.WriteStartElement("a", "solidFill", Ns.A);
            w.WriteStartElement("a", "schemeClr", Ns.A);
            w.WriteAttributeString("val", "phClr");
            w.WriteEndElement();
            w.WriteEndElement();
        }
        w.WriteEndElement();

        // Line style list (3 required)
        w.WriteStartElement("a", "lnStyleLst", Ns.A);
        for (var i = 0; i < 3; i++)
        {
            w.WriteStartElement("a", "ln", Ns.A);
            w.WriteAttributeString("w", ((i + 1) * 6350).ToString());
            w.WriteStartElement("a", "solidFill", Ns.A);
            w.WriteStartElement("a", "schemeClr", Ns.A);
            w.WriteAttributeString("val", "phClr");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
        }
        w.WriteEndElement();

        // Effect style list (3 required)
        w.WriteStartElement("a", "effectStyleLst", Ns.A);
        for (var i = 0; i < 3; i++)
        {
            w.WriteStartElement("a", "effectStyle", Ns.A);
            w.WriteStartElement("a", "effectLst", Ns.A);
            w.WriteEndElement();
            w.WriteEndElement();
        }
        w.WriteEndElement();

        // Background fill style list (3 required)
        w.WriteStartElement("a", "bgFillStyleLst", Ns.A);
        for (var i = 0; i < 3; i++)
        {
            w.WriteStartElement("a", "solidFill", Ns.A);
            w.WriteStartElement("a", "schemeClr", Ns.A);
            w.WriteAttributeString("val", "phClr");
            w.WriteEndElement();
            w.WriteEndElement();
        }
        w.WriteEndElement();

        w.WriteEndElement(); // fmtScheme
    }

    private static void WriteSrgbColor(XmlWriter w, string element, string hex)
    {
        w.WriteStartElement("a", element, Ns.A);
        w.WriteStartElement("a", "srgbClr", Ns.A);
        w.WriteAttributeString("val", hex);
        w.WriteEndElement();
        w.WriteEndElement();
    }

    /// <summary>Darken a hex color by a factor (0-1).</summary>
    private static string DarkenColor(string hex, double factor)
    {
        var (r, g, b) = ParseHex(hex);
        r = (int)(r * (1 - factor));
        g = (int)(g * (1 - factor));
        b = (int)(b * (1 - factor));
        return $"{r:X2}{g:X2}{b:X2}";
    }

    /// <summary>Lighten a hex color by a factor (0-1).</summary>
    private static string LightenColor(string hex, double factor)
    {
        var (r, g, b) = ParseHex(hex);
        r = (int)(r + (255 - r) * factor);
        g = (int)(g + (255 - g) * factor);
        b = (int)(b + (255 - b) * factor);
        return $"{r:X2}{g:X2}{b:X2}";
    }

    private static (int R, int G, int B) ParseHex(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length < 6) return (0, 0, 0);
        return (
            Convert.ToInt32(hex[..2], 16),
            Convert.ToInt32(hex[2..4], 16),
            Convert.ToInt32(hex[4..6], 16)
        );
    }
}
