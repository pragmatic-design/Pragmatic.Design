using System.Xml;
using System.Xml.Linq;
using Pragmatic.Documents.Model;
using Pragmatic.Documents.Templating;
using Pragmatic.Documents.Templates;
using Pragmatic.Documents.Templates.Nodes;

namespace Pragmatic.Documents.Markup;

/// <summary>
/// Parses PDX-Doc markup (XML-like) into <see cref="DocumentTemplate"/>.
/// </summary>
public static class PdxDocParser
{
    // XXE-safe reader settings: DTD forbidden (blocks billion-laughs internal entity expansion)
    // and no external resolver (blocks external entity fetches). MaxCharactersInDocument bounds total
    // input so a runaway/huge markup file cannot exhaust memory (and transitively caps nesting depth).
    private static readonly XmlReaderSettings SafeXmlSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        MaxCharactersInDocument = 10_000_000,
    };

    public static DocumentTemplate Parse(string markup)
    {
        using var stringReader = new StringReader(markup);
        using var xmlReader = XmlReader.Create(stringReader, SafeXmlSettings);
        var doc = XDocument.Load(xmlReader);
        var root = doc.Root ?? throw new MarkupParseException("Empty markup");

        if (root.Name.LocalName != "document")
            throw new MarkupParseException($"Root element must be <document>, got <{root.Name.LocalName}>");

        MarkupVocabulary.RequireKnownAttributes(root,
            "title", "author", "lang", "page-size", "orientation", "margin", "page-data-source", "page-item");

        return new DocumentTemplate
        {
            Title = Attr(root, "title"),
            Author = Attr(root, "author"),
            Language = Attr(root, "lang"),
            PageSize = ParsePageSize(Attr(root, "page-size")),
            Orientation = ParseOrientation(Attr(root, "orientation")),
            Margins = ParseMargins(Attr(root, "margin")),
            PageDataSource = Attr(root, "page-data-source"),
            PageItemName = Attr(root, "page-item") ?? "item",
            Pages = ParsePages(root)
        };
    }

    public static DocumentTemplate ParseFile(string filePath)
        => Parse(File.ReadAllText(filePath));

    private static List<DocumentPageTemplate> ParsePages(XElement root)
    {
        var pages = new List<DocumentPageTemplate>();

        foreach (var pageEl in root.Elements().Where(e => e.Name.LocalName == "page"))
        {
            pages.Add(ParsePage(pageEl));
        }

        // With explicit pages, content beside them belongs to none of them.
        if (pages.Count > 0 && root.Elements().FirstOrDefault(e => e.Name.LocalName != "page") is { } stray)
            throw MarkupVocabulary.UnknownElement(stray, "<document> beside its <page> elements");

        // If no explicit <page> tags, treat all children as content of a single page
        if (pages.Count == 0)
        {
            var content = ParseContentNodes(root);
            if (content.Count > 0)
                pages.Add(new DocumentPageTemplate { Content = content });
        }

        return pages;
    }

    private static DocumentPageTemplate ParsePage(XElement pageEl)
    {
        List<DocumentNodeTemplate>? header = null;
        List<DocumentNodeTemplate>? footer = null;
        var content = new List<DocumentNodeTemplate>();
        MarkupVocabulary.RequireKnownAttributes(pageEl);

        foreach (var child in pageEl.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "header":
                    MarkupVocabulary.RequireKnownAttributes(child);
                    header = ParseContentNodes(child);
                    break;
                case "footer":
                    MarkupVocabulary.RequireKnownAttributes(child);
                    footer = ParseContentNodes(child);
                    break;
                default:
                    // <import> falls through to ParseNode, which maps it to a PartialTemplate.
                    content.Add(ParseNode(child));
                    break;
            }
        }

        return new DocumentPageTemplate
        {
            Header = header,
            Footer = footer,
            Content = content
        };
    }

    private static List<DocumentNodeTemplate> ParseContentNodes(XElement parent)
    {
        var nodes = new List<DocumentNodeTemplate>();
        foreach (var el in parent.Elements())
        {
            // <import> is mapped to a PartialTemplate by ParseNode (include-by-src semantics).
            nodes.Add(ParseNode(el));
        }
        return nodes;
    }

    private static DocumentNodeTemplate ParseNode(XElement el)
    {
        MarkupVocabulary.RequireKnownAttributes(el, [.. MarkupVocabulary.Directives, .. NodeAttributes(el)]);
        var directives = ParseDirectives(el);

        return el.Name.LocalName switch
        {
            "text" => ParseText(el, directives),
            "heading" => new HeadingTemplate
            {
                Content = el.Value,
                Level = IntAttr(el, "level", 1),
                Directives = directives
            },
            "paragraph" => new ParagraphTemplate
            {
                Children = ParseContentNodes(el),
                Directives = directives
            },
            "image" => new ImageTemplate
            {
                Source = Attr(el, "src") ?? "",
                Alt = Attr(el, "alt"),
                Width = DoubleAttr(el, "width"),
                Height = DoubleAttr(el, "height"),
                Directives = directives
            },
            "table" => ParseTable(el, directives),
            "list" => ParseList(el, directives),
            "hr" => new HorizontalRuleTemplate
            {
                Thickness = DoubleAttr(el, "thickness") ?? 0.5,
                Directives = directives
            },
            "spacer" => new SpacerTemplate
            {
                Height = DoubleAttr(el, "height") ?? 10,
                Directives = directives
            },
            "container" => new ContainerTemplate
            {
                Children = ParseContentNodes(el),
                Directives = directives
            },
            "pagebreak" => new PageBreakTemplate { Directives = directives },
            "barcode" => new BarcodeTemplate
            {
                Value = Attr(el, "value") ?? "",
                Type = ParseBarcodeType(Attr(el, "type")),
                Width = DoubleAttr(el, "width"),
                Height = DoubleAttr(el, "height"),
                Directives = directives
            },
            "for-each" => new ForEachTemplate
            {
                DataSource = Attr(el, "source") ?? throw new MarkupParseException("<for-each> requires 'source' attribute"),
                ItemName = Attr(el, "item") ?? "item",
                Children = ParseContentNodes(el),
                Directives = directives
            },
            "partial" => new PartialTemplate
            {
                Name = Attr(el, "name") ?? throw new MarkupParseException("<partial> requires 'name' attribute"),
                Directives = directives
            },
            // <import src="header.pdxdoc" /> includes a partial registered under its src name
            // (see docs/markup-parser.md). It resolves identically to <partial name="...">.
            "import" => new PartialTemplate
            {
                Name = Attr(el, "src") ?? throw new MarkupParseException("<import> requires 'src' attribute"),
                Directives = directives
            },
            _ => throw MarkupVocabulary.UnknownElement(el, "a document")
        };
    }

    /// <summary>What each content node reads besides the directives — the same names <see cref="ParseNode" /> asks for.</summary>
    private static string[] NodeAttributes(XElement el) => el.Name.LocalName switch
    {
        "heading" => ["level"],
        "image" => ["src", "alt", "width", "height"],
        "table" => ["data-source", "repeat-header"],
        "list" => ["ordered", "data-source"],
        "hr" => ["thickness"],
        "spacer" => ["height"],
        "barcode" => ["value", "type", "width", "height"],
        "for-each" => ["source", "item"],
        "partial" => ["name"],
        "import" => ["src"],
        _ => []
    };

    private static TextTemplate ParseText(XElement el, TemplateDirectives? directives)
    {
        // TextTemplate doesn't have style properties directly — they go through NodeStyle on the model.
        // For the markup, we capture the content and basic style hints.
        // The resolver maps TextTemplate.Content → TextNode.Content.
        // Style attributes (color, font-size, etc.) are informational for future style support.
        return new TextTemplate
        {
            Content = el.Value,
            Directives = directives
        };
    }

    private static TableTemplate ParseTable(XElement el, TemplateDirectives? directives)
    {
        var columns = new List<TableColumn>();
        TableRowTemplate? header = null;
        TableRowTemplate? rowTemplate = null;
        var staticRows = new List<TableRowTemplate>();

        var columnElements = el.Elements().Where(e => e.Name.LocalName == "column").ToList();

        foreach (var colEl in columnElements)
        {
            MarkupVocabulary.RequireKnownAttributes(colEl, "width", "align");
            columns.Add(new TableColumn
            {
                Width = DoubleAttr(colEl, "width"),
                Align = ParseTextAlign(Attr(colEl, "align"))
            });
        }

        // If columns have text content, use as header
        if (columnElements.Any(ce => !string.IsNullOrWhiteSpace(ce.Value)))
        {
            header = new TableRowTemplate
            {
                Cells = columnElements.Select(ce => new TableCellTemplate
                {
                    Content = [new TextTemplate { Content = ce.Value.Trim() }]
                }).ToList()
            };
        }

        foreach (var child in el.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "column":
                    break; // Already processed
                case "row-template":
                    rowTemplate = ParseDocTableRow(child);
                    break;
                case "row":
                    staticRows.Add(ParseDocTableRow(child));
                    break;
                case "header":
                    header = ParseDocTableRow(child);
                    break;
                default:
                    throw MarkupVocabulary.UnknownElement(child, "<table>");
            }
        }

        return new TableTemplate
        {
            Columns = columns,
            Header = header,
            Rows = staticRows,
            DataSource = Attr(el, "data-source"),
            RowTemplate = rowTemplate,
            RepeatHeader = BoolAttr(el, "repeat-header", true),
            Directives = directives
        };
    }

    private static TableRowTemplate ParseDocTableRow(XElement el)
    {
        MarkupVocabulary.RequireKnownAttributes(el);
        var cells = el.Elements()
            .Select(cell => cell.Name.LocalName == "cell"
                ? cell
                : throw MarkupVocabulary.UnknownElement(cell, $"<{el.Name.LocalName}>"))
            .Select(cell =>
            {
                MarkupVocabulary.RequireKnownAttributes(cell, "colspan", "rowspan");
                return new TableCellTemplate
                {
                    Content = cell.HasElements
                        ? ParseContentNodes(cell)
                        : [new TextTemplate { Content = cell.Value }],
                    ColSpan = IntAttr(cell, "colspan", 1),
                    RowSpan = IntAttr(cell, "rowspan", 1)
                };
            })
            .ToList();

        return new TableRowTemplate { Cells = cells };
    }

    private static ListTemplate ParseList(XElement el, TemplateDirectives? directives)
    {
        if (el.Elements().FirstOrDefault(e => e.Name.LocalName is not ("list-item" or "item-template")) is { } stray)
            throw MarkupVocabulary.UnknownElement(stray, "<list>");

        var items = el.Elements()
            .Where(e => e.Name.LocalName == "list-item")
            .Select(item =>
            {
                MarkupVocabulary.RequireKnownAttributes(item);
                return new ListItemTemplate
                {
                    Content = item.HasElements
                        ? ParseContentNodes(item)
                        : [new TextTemplate { Content = item.Value }]
                };
            })
            .ToList();

        // Data-bound list
        ListItemTemplate? itemTemplate = null;
        var templateEl = el.Elements().FirstOrDefault(e => e.Name.LocalName == "item-template");
        if (templateEl is not null)
        {
            MarkupVocabulary.RequireKnownAttributes(templateEl);
            itemTemplate = new ListItemTemplate
            {
                Content = templateEl.HasElements
                    ? ParseContentNodes(templateEl)
                    : [new TextTemplate { Content = templateEl.Value }]
            };
        }

        return new ListTemplate
        {
            Ordered = BoolAttr(el, "ordered", false),
            Items = items,
            DataSource = Attr(el, "data-source"),
            ItemTemplate = itemTemplate,
            Directives = directives
        };
    }

    // --- Helpers ---

    private static TemplateDirectives? ParseDirectives(XElement el)
    {
        var ifExpr = Attr(el, "if");
        var forExpr = Attr(el, "for");
        if (ifExpr is null && forExpr is null) return null;
        return new TemplateDirectives { If = ifExpr, For = forExpr };
    }

    private static string? Attr(XElement el, string name) => el.Attribute(name)?.Value;

    private static int IntAttr(XElement el, string name, int defaultValue)
        => int.TryParse(el.Attribute(name)?.Value, out var v) ? v : defaultValue;

    private static double? DoubleAttr(XElement el, string name)
        => double.TryParse(el.Attribute(name)?.Value, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : null;

    private static bool BoolAttr(XElement el, string name, bool defaultValue)
        => el.Attribute(name)?.Value?.ToLowerInvariant() switch
        {
            "true" or "1" => true,
            "false" or "0" => false,
            _ => defaultValue
        };

    private static PageSize ParsePageSize(string? value) => value?.ToUpperInvariant() switch
    {
        "A3" => PageSize.A3,
        "A5" => PageSize.A5,
        "LETTER" => PageSize.Letter,
        "LEGAL" => PageSize.Legal,
        _ => PageSize.A4
    };

    private static PageOrientation ParseOrientation(string? value) => value?.ToLowerInvariant() switch
    {
        "landscape" => PageOrientation.Landscape,
        _ => PageOrientation.Portrait
    };

    private static Margins ParseMargins(string? value)
    {
        if (value is null) return Margins.Default;
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length switch
        {
            1 when double.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var u)
                => new Margins { Top = u, Right = u, Bottom = u, Left = u },
            2 when double.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var v) &&
                   double.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var h)
                => new Margins { Top = v, Right = h, Bottom = v, Left = h },
            4 when double.TryParse(parts[0], System.Globalization.CultureInfo.InvariantCulture, out var t) &&
                   double.TryParse(parts[1], System.Globalization.CultureInfo.InvariantCulture, out var r) &&
                   double.TryParse(parts[2], System.Globalization.CultureInfo.InvariantCulture, out var b) &&
                   double.TryParse(parts[3], System.Globalization.CultureInfo.InvariantCulture, out var l)
                => new Margins { Top = t, Right = r, Bottom = b, Left = l },
            _ => Margins.Default
        };
    }

    private static TextAlign? ParseTextAlign(string? value) => value?.ToLowerInvariant() switch
    {
        "center" => TextAlign.Center,
        "right" => TextAlign.Right,
        "justify" => TextAlign.Justify,
        "left" => TextAlign.Left,
        _ => null
    };

    private static BarcodeType ParseBarcodeType(string? value) => value?.ToLowerInvariant() switch
    {
        "code128" => BarcodeType.Code128,
        "code39" => BarcodeType.Code39,
        "ean13" => BarcodeType.Ean13,
        "ean8" => BarcodeType.Ean8,
        _ => BarcodeType.QrCode
    };
}
