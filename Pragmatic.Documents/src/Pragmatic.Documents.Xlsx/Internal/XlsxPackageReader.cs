using System.Globalization;
using System.IO.Compression;
using System.Xml;
using System.Xml.Linq;
using Pragmatic.Documents.Ooxml;
using Pragmatic.Documents.Spreadsheet;

namespace Pragmatic.Documents.Xlsx.Internal;

/// <summary>Reads an XLSX ZIP package into a SpreadsheetModel.</summary>
internal static class XlsxPackageReader
{
    private static readonly XNamespace Ss = Ns.SS;
    private static readonly XNamespace Rns = Ns.R;
    private static readonly XNamespace DcNs = Ns.DC;

    // Safety limits for untrusted input
    private const int MaxSheetCount = 256;
    private const int MaxSharedStringCount = 1_000_000;
    private const long MaxEntryBytes = 100 * 1024 * 1024; // 100 MB per entry
    // The model is in-memory by design; this bounds the number of Cell objects a single sheet can
    // materialize (a 100 MB worksheet of tiny cells would otherwise expand to GBs of model).
    private const int MaxCellsPerSheet = 5_000_000;

    // XXE-safe reader settings for all XDocument.Load calls: DTD forbidden (blocks
    // billion-laughs entity-expansion bombs) and no external resolver (blocks external
    // entity fetches). XLSX is ZIP-of-XML — every internal part must be parsed this way.
    private static readonly XmlReaderSettings SafeXmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    private static XDocument LoadXmlSafely(Stream stream)
    {
        using var reader = XmlReader.Create(stream, SafeXmlSettings);
        return XDocument.Load(reader);
    }

    private static bool IsSafeWorksheetTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) return false;
        // Absolute URIs, parent-traversal, and UNC-style prefixes all escape the expected
        // xl/worksheets/ subdirectory once concatenated with the "xl/" base.
        if (target.StartsWith('/') || target.StartsWith('\\')) return false;
        if (target.Contains("..", StringComparison.Ordinal)) return false;
        return target.StartsWith("worksheets/", StringComparison.OrdinalIgnoreCase);
    }

    internal static SpreadsheetModel Read(Stream stream)
    {
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);

        var sharedStrings = ReadSharedStrings(zip);
        var (title, author) = ReadCoreProperties(zip);
        var sheets = ReadWorkbook(zip, sharedStrings);

        return new SpreadsheetModel
        {
            Title = title,
            Author = author,
            Sheets = sheets
        };
    }

    private static string[] ReadSharedStrings(ZipArchive zip)
    {
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return [];
        GuardEntrySize(entry);

        // Streaming parse: one <si> element in memory at a time instead of the whole DOM —
        // a 100 MB part would otherwise expand to a multi-GB tree before the count guard runs.
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, SafeXmlSettings);

        var strings = new List<string>();
        reader.MoveToContent();
        while (!reader.EOF)
        {
            if (reader is { NodeType: XmlNodeType.Element, LocalName: "si" })
            {
                if (strings.Count >= MaxSharedStringCount)
                    throw new InvalidOperationException(
                        $"XLSX shared string count exceeds limit ({MaxSharedStringCount}).");

                var si = (XElement)XNode.ReadFrom(reader);
                strings.Add(si.Element(Ss + "t")?.Value ?? ConcatRichText(si));
            }
            else
            {
                reader.Read();
            }
        }

        return strings.ToArray();
    }

    private static string ConcatRichText(XElement si)
    {
        // Rich text: <si><r><t>part1</t></r><r><t>part2</t></r></si>
        return string.Concat(si.Descendants(Ss + "t").Select(t => t.Value));
    }

    private static (string? Title, string? Author) ReadCoreProperties(ZipArchive zip)
    {
        var entry = zip.GetEntry("docProps/core.xml");
        if (entry is null) return (null, null);
        GuardEntrySize(entry);

        using var stream = entry.Open();
        var doc = LoadXmlSafely(stream);
        var title = doc.Root!.Element(DcNs + "title")?.Value;
        var author = doc.Root!.Element(DcNs + "creator")?.Value;
        return (title, author);
    }

    private static List<Sheet> ReadWorkbook(ZipArchive zip, string[] sharedStrings)
    {
        var wbEntry = zip.GetEntry("xl/workbook.xml");
        if (wbEntry is null) return [];
        GuardEntrySize(wbEntry);

        using var wbStream = wbEntry.Open();
        var wbDoc = LoadXmlSafely(wbStream);

        // Read workbook relationships to map rId → sheet path
        var relMap = ReadRelationships(zip, "xl/_rels/workbook.xml.rels");

        var sheetElements = wbDoc.Root!.Descendants(Ss + "sheet").ToList();
        if (sheetElements.Count > MaxSheetCount)
            throw new InvalidOperationException(
                $"XLSX sheet count ({sheetElements.Count}) exceeds limit ({MaxSheetCount}).");

        var sheets = new List<Sheet>();
        foreach (var sheetEl in sheetElements)
        {
            var name = sheetEl.Attribute("name")?.Value ?? "Sheet";
            var rId = sheetEl.Attribute(Rns + "id")?.Value;

            if (rId is not null && relMap.TryGetValue(rId, out var target))
            {
                // Reject relationship targets that try to reach outside xl/worksheets/ or that use
                // traversal. Without this a crafted XLSX could point a worksheet rId at an unrelated
                // package part (docProps, _rels) and have the reader parse it as a sheet.
                if (!IsSafeWorksheetTarget(target))
                    throw new InvalidOperationException(
                        $"XLSX relationship target '{target}' is not inside xl/worksheets/.");

                var sheetPath = $"xl/{target}";
                var sheet = ReadWorksheet(zip, sheetPath, name, sharedStrings);
                sheets.Add(sheet);
            }
        }

        return sheets;
    }

    private static Dictionary<string, string> ReadRelationships(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        if (entry is null) return [];

        GuardEntrySize(entry);
        using var stream = entry.Open();
        var doc = LoadXmlSafely(stream);
        XNamespace relNs = Ns.Rel;

        // A relationship is only usable if it has BOTH Id and Target. A malformed package may omit
        // either; skip such entries instead of dereferencing a null attribute (a `!.Value`
        // null-forgiving access would throw NullReferenceException).
        var map = new Dictionary<string, string>();
        foreach (var el in doc.Root!.Elements(relNs + "Relationship"))
        {
            var id = el.Attribute("Id")?.Value;
            var target = el.Attribute("Target")?.Value;
            if (id is null || target is null) continue;
            map[id] = target;
        }
        return map;
    }

    private static Sheet ReadWorksheet(ZipArchive zip, string path, string name, string[] sharedStrings)
    {
        var entry = zip.GetEntry(path);
        if (entry is null) return new Sheet { Name = name };
        GuardEntrySize(entry);

        // Streaming parse: one element in memory at a time (rows are the unbounded part —
        // pane/col/mergeCell are tiny). Loading the whole worksheet DOM would expand a 100 MB
        // part to a multi-GB tree before any model guard could run.
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, SafeXmlSettings);

        var rows = new List<Row>();
        var cellCount = 0;
        FrozenPane? frozenPane = null;
        List<MergeRange>? mergedCells = null;
        List<Column>? columns = null;

        reader.MoveToContent();
        while (!reader.EOF)
        {
            if (reader.NodeType != XmlNodeType.Element)
            {
                reader.Read();
                continue;
            }

            switch (reader.LocalName)
            {
                case "row":
                {
                    var rowEl = (XElement)XNode.ReadFrom(reader);
                    var cells = new List<Cell>();
                    foreach (var cellEl in rowEl.Elements(Ss + "c"))
                    {
                        if (++cellCount > MaxCellsPerSheet)
                            throw new InvalidOperationException(
                                $"XLSX sheet '{name}' cell count exceeds limit ({MaxCellsPerSheet}).");
                        cells.Add(ReadCell(cellEl, sharedStrings));
                    }

                    var height = ParseDouble(rowEl.Attribute("ht")?.Value);
                    var hidden = rowEl.Attribute("hidden")?.Value == "1";
                    rows.Add(new Row(cells, height, hidden));
                    continue;
                }

                case "pane":
                {
                    var paneEl = (XElement)XNode.ReadFrom(reader);
                    if (paneEl.Attribute("state")?.Value == "frozen")
                    {
                        var ySplit = (int?)ParseDouble(paneEl.Attribute("ySplit")?.Value) ?? 0;
                        var xSplit = (int?)ParseDouble(paneEl.Attribute("xSplit")?.Value) ?? 0;
                        frozenPane ??= new FrozenPane(ySplit, xSplit);
                    }
                    continue;
                }

                case "col":
                {
                    var colEl = (XElement)XNode.ReadFrom(reader);
                    (columns ??= []).Add(new Column(
                        Width: ParseDouble(colEl.Attribute("width")?.Value),
                        Hidden: colEl.Attribute("hidden")?.Value == "1"));
                    continue;
                }

                case "mergeCell":
                {
                    var mergeEl = (XElement)XNode.ReadFrom(reader);
                    var refAttr = mergeEl.Attribute("ref")?.Value ?? "";
                    var parts = refAttr.Split(':');
                    (mergedCells ??= []).Add(parts.Length == 2
                        ? new MergeRange(parts[0], parts[1])
                        : new MergeRange(refAttr, refAttr));
                    continue;
                }

                default:
                    reader.Read();
                    continue;
            }
        }

        return new Sheet
        {
            Name = name,
            Rows = rows,
            FrozenPane = frozenPane,
            MergedCells = mergedCells?.Count > 0 ? mergedCells : null,
            Columns = columns?.Count > 0 ? columns : null
        };
    }

    private static Cell ReadCell(XElement cellEl, string[] sharedStrings)
    {
        var type = cellEl.Attribute("t")?.Value;
        var formula = cellEl.Element(Ss + "f")?.Value;
        var valueStr = cellEl.Element(Ss + "v")?.Value;

        object? value = null;
        if (valueStr is not null)
        {
            value = type switch
            {
                "s" => int.TryParse(valueStr, out var idx) && idx >= 0 && idx < sharedStrings.Length
                    ? sharedStrings[idx]
                    : valueStr,
                "b" => valueStr == "1",
                _ => ParseNumber(valueStr)
            };
        }

        return new Cell
        {
            Value = value,
            Formula = formula
        };
    }

    private static object? ParseNumber(string value)
    {
        // Try long first for large integers (preserves precision beyond double's 53-bit mantissa)
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l))
        {
            if (l is >= int.MinValue and <= int.MaxValue)
                return (int)l;
            return l;
        }

        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return d;
        return value;
    }

    private static double? ParseDouble(string? value)
    {
        if (value is null) return null;
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static void GuardEntrySize(ZipArchiveEntry entry)
    {
        // ZipArchiveEntry.Length is the uncompressed size (available for read-mode archives)
        if (entry.Length > MaxEntryBytes)
            throw new InvalidOperationException(
                $"XLSX entry '{entry.FullName}' uncompressed size ({entry.Length:N0} bytes) exceeds limit ({MaxEntryBytes:N0} bytes).");
    }
}
