namespace Pragmatic.Documents.Ooxml;

/// <summary>OOXML namespace URIs shared between DOCX and XLSX.</summary>
public static class Ns
{
    public const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    public const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public const string WP = "http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing";
    public const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    public const string PIC = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    public const string CT = "http://schemas.openxmlformats.org/package/2006/content-types";
    public const string Rel = "http://schemas.openxmlformats.org/package/2006/relationships";
    public const string DC = "http://purl.org/dc/elements/1.1/";
    public const string DCTERMS = "http://purl.org/dc/terms/";
    public const string CP = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
    public const string XSI = "http://www.w3.org/2001/XMLSchema-instance";

    // Spreadsheet-specific
    public const string SS = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    // Relationship types (shared)
    public const string RelDocument = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument";
    public const string RelCoreProps = "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties";
    public const string RelAppProps = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties";
    public const string RelStyles = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles";
    public const string RelNumbering = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/numbering";
    public const string RelSettings = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/settings";
    public const string RelFontTable = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/fontTable";
    public const string RelHeader = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/header";
    public const string RelFooter = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/footer";
    public const string RelFootnotes = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/footnotes";
    public const string RelImage = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image";
    public const string RelHyperlink = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/hyperlink";
    public const string RelTheme = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme";

    // XLSX-specific relationship types
    public const string RelWorksheet = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet";
    public const string RelSharedStrings = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/sharedStrings";
}
