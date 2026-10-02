using System.Xml;
using System.Xml.Linq;
using Pragmatic.Documents.Templating;
using Pragmatic.Email.Model;
using Pragmatic.Email.Templates;
using Pragmatic.Email.Templates.Nodes;

namespace Pragmatic.Documents.Markup;

/// <summary>
/// Parses PDX-Email markup (XML-like, Bootstrap grid) into <see cref="EmailTemplate"/>.
/// </summary>
public static class PdxEmailParser
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

    public static EmailTemplate Parse(string markup)
    {
        using var stringReader = new StringReader(markup);
        using var xmlReader = XmlReader.Create(stringReader, SafeXmlSettings);
        var doc = XDocument.Load(xmlReader);
        var root = doc.Root ?? throw new MarkupParseException("Empty markup");

        if (root.Name.LocalName != "email")
            throw new MarkupParseException($"Root element must be <email>, got <{root.Name.LocalName}>");

        MarkupVocabulary.RequireKnownAttributes(root,
            "subject", "preheader", "lang", "width", "background", "wrapper-background", "font-family",
            "font-size", "text-color");

        return new EmailTemplate
        {
            Subject = Attr(root, "subject"),
            Preheader = Attr(root, "preheader"),
            Language = Attr(root, "lang"),
            Width = IntAttr(root, "width", 600),
            BackgroundColor = Attr(root, "background") ?? "#ffffff",
            WrapperBackgroundColor = Attr(root, "wrapper-background"),
            FontFamily = Attr(root, "font-family") ?? "Arial, Helvetica, sans-serif",
            FontSize = IntAttr(root, "font-size", 16),
            TextColor = Attr(root, "text-color") ?? "#333333",
            Sections = ParseChildren(root)
        };
    }

    public static EmailTemplate ParseFile(string filePath)
        => Parse(File.ReadAllText(filePath));

    private static List<EmailSectionTemplate> ParseChildren(XElement parent)
    {
        var sections = new List<EmailSectionTemplate>();

        foreach (var el in parent.Elements())
        {
            if (el.Name.LocalName is "hero" or "row" or "article" or "footer")
                MarkupVocabulary.RequireKnownAttributes(el, [.. MarkupVocabulary.Directives, "background", "padding"]);

            switch (el.Name.LocalName)
            {
                case "hero":
                    sections.Add(ParseHero(el));
                    break;
                case "row":
                    sections.Add(ParseRow(el));
                    break;
                case "article":
                    sections.Add(ParseArticle(el));
                    break;
                case "footer":
                    sections.Add(ParseFooter(el));
                    break;
                default:
                    // Single-element section (text, image, etc. at top level) — a partial included.
                    // A partial is resolved as a node inside a column, so one skipped here would never
                    // be resolved: at this level it is a section of its own, like any other single
                    // element.
                    sections.Add(WrapInSection(el, null));
                    break;
            }
        }

        return sections;
    }

    private static EmailSectionTemplate ParseHero(XElement el)
    {
        var nodes = ParseContentNodes(el);
        // Center all text/heading nodes in hero
        var centeredNodes = nodes.Select(n => n switch
        {
            EmailHeadingTemplate h when h.Align == EmailTextAlign.Left => h with { Align = EmailTextAlign.Center },
            EmailTextTemplate t when t.Align == EmailTextAlign.Left => t with { Align = EmailTextAlign.Center },
            _ => n
        }).ToList();

        return new EmailSectionTemplate
        {
            Directives = ParseDirectives(el),
            BackgroundColor = Attr(el, "background"),
            Padding = ParsePadding(el, EmailPadding.All(30)),
            Columns = [new EmailColumnTemplate { Content = centeredNodes }]
        };
    }

    private static EmailSectionTemplate ParseRow(XElement el)
    {
        var columns = new List<EmailColumnTemplate>();

        foreach (var colEl in el.Elements().Where(e => e.Name.LocalName == "col"))
        {
            MarkupVocabulary.RequireKnownAttributes(colEl, "width", "valign", "padding");
            var gridWidth = IntAttr(colEl, "width", 12);
            var fraction = gridWidth / 12.0;

            columns.Add(new EmailColumnTemplate
            {
                Width = fraction,
                VerticalAlign = ParseVerticalAlign(Attr(colEl, "valign")),
                Padding = ParseOptionalPadding(colEl),
                Content = ParseContentNodes(colEl)
            });
        }

        // With columns, content beside them belongs to none of them.
        if (columns.Count > 0 && el.Elements().FirstOrDefault(e => e.Name.LocalName != "col") is { } stray)
            throw MarkupVocabulary.UnknownElement(stray, "<row> beside its <col> elements");

        // If no <col> children, wrap all content in a single full-width column
        if (columns.Count == 0)
        {
            columns.Add(new EmailColumnTemplate
            {
                Content = ParseContentNodes(el)
            });
        }

        return new EmailSectionTemplate
        {
            Directives = ParseDirectives(el),
            BackgroundColor = Attr(el, "background"),
            Padding = ParsePadding(el, EmailPadding.Default),
            Columns = columns
        };
    }

    private static EmailSectionTemplate ParseArticle(XElement el)
    {
        // Article = full-width section with image + heading(H2) + text + button
        var nodes = new List<EmailNodeTemplate>();

        foreach (var child in el.Elements())
        {
            var node = ParseNode(child);

            // Default heading level to 2 for articles
            if (node is EmailHeadingTemplate h && h.Level == 1)
                node = h with { Level = 2 };
            nodes.Add(node);
        }

        return new EmailSectionTemplate
        {
            Directives = ParseDirectives(el),
            BackgroundColor = Attr(el, "background"),
            Padding = ParsePadding(el, EmailPadding.Default),
            Columns = [new EmailColumnTemplate { Content = nodes }]
        };
    }

    private static EmailSectionTemplate ParseFooter(XElement el)
    {
        return new EmailSectionTemplate
        {
            Directives = ParseDirectives(el),
            BackgroundColor = Attr(el, "background"),
            Padding = ParsePadding(el, EmailPadding.All(10)),
            Columns = [new EmailColumnTemplate { Content = ParseContentNodes(el) }]
        };
    }

    private static EmailSectionTemplate WrapInSection(XElement el, string? background)
    {
        var node = ParseNode(el);

        return new EmailSectionTemplate
        {
            BackgroundColor = background,
            Columns = [new EmailColumnTemplate { Content = [node] }]
        };
    }

    private static List<EmailNodeTemplate> ParseContentNodes(XElement parent)
    {
        var nodes = new List<EmailNodeTemplate>();
        foreach (var el in parent.Elements())
        {
            // ⚠️ Only `col` is skipped: a column is read by the row that owns it. Skipping `import` too
            // would make the document's spelling of a partial render nothing and say nothing.
            if (el.Name.LocalName is "col") continue;
            nodes.Add(ParseNode(el));
        }
        return nodes;
    }

    private static EmailNodeTemplate ParseNode(XElement el)
    {
        MarkupVocabulary.RequireKnownAttributes(el, [.. MarkupVocabulary.Directives, .. NodeAttributes(el)]);
        var directives = ParseDirectives(el);

        return el.Name.LocalName switch
        {
            "text" => new EmailTextTemplate
            {
                Content = el.Value,
                Align = ParseTextAlign(Attr(el, "align")),
                Color = Attr(el, "color"),
                FontSize = NullableIntAttr(el, "size"),
                Directives = directives
            },
            "heading" => new EmailHeadingTemplate
            {
                Content = el.Value,
                Level = IntAttr(el, "level", 1),
                Align = ParseTextAlign(Attr(el, "align")),
                Color = Attr(el, "color"),
                Directives = directives
            },
            "image" => new EmailImageTemplate
            {
                Source = Attr(el, "src") ?? "",
                Alt = Attr(el, "alt") ?? "",
                Width = NullableIntAttr(el, "width"),
                Height = NullableIntAttr(el, "height"),
                Link = Attr(el, "link"),
                Align = ParseTextAlign(Attr(el, "align")),
                Directives = directives
            },
            "button" => new EmailButtonTemplate
            {
                Text = el.Value,
                Href = Attr(el, "href") ?? "",
                BackgroundColor = Attr(el, "background") ?? "#007bff",
                TextColor = Attr(el, "color") ?? "#ffffff",
                BorderRadius = IntAttr(el, "radius", 4),
                FontSize = IntAttr(el, "font-size", 16),
                Align = ParseTextAlign(Attr(el, "align")),
                Directives = directives
            },
            "spacer" => new EmailSpacerTemplate
            {
                Height = IntAttr(el, "height", 20),
                Directives = directives
            },
            "divider" => new EmailDividerTemplate
            {
                Color = Attr(el, "color") ?? "#cccccc",
                Thickness = IntAttr(el, "thickness", 1),
                Directives = directives
            },
            "table" => ParseTable(el, directives),
            "partial" => new EmailPartialTemplate
            {
                Name = Attr(el, "name") ?? throw new MarkupParseException("<partial> requires 'name' attribute"),
                Directives = directives
            },
            // <import src="mail-header.pdxemail" /> is the same partial under the spelling a .pdxdoc
            // uses, and PdxDocParser maps both the same way. What to make of the name — a file name, a
            // key — is the partial provider's business, exactly as it is for a document.
            "import" => new EmailPartialTemplate
            {
                Name = Attr(el, "src") ?? throw new MarkupParseException("<import> requires 'src' attribute"),
                Directives = directives
            },
            _ => throw MarkupVocabulary.UnknownElement(el, "an email")
        };
    }

    /// <summary>What each content node reads besides the directives — the same names <see cref="ParseNode" /> asks for.</summary>
    private static string[] NodeAttributes(XElement el) => el.Name.LocalName switch
    {
        "text" => ["align", "color", "size"],
        "heading" => ["level", "align", "color"],
        "image" => ["src", "alt", "width", "height", "link", "align"],
        "button" => ["href", "background", "color", "radius", "font-size", "align"],
        "spacer" => ["height"],
        "divider" => ["color", "thickness"],
        "table" => ["data-source", "border", "padding"],
        "partial" => ["name"],
        "import" => ["src"],
        _ => []
    };

    private static EmailTableTemplate ParseTable(XElement el, TemplateDirectives? directives)
    {
        var columns = new List<EmailTableColumn>();
        EmailTableRowTemplate? rowTemplate = null;
        var staticRows = new List<EmailTableRowTemplate>();

        foreach (var child in el.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "column":
                    MarkupVocabulary.RequireKnownAttributes(child, "width", "align");
                    columns.Add(new EmailTableColumn
                    {
                        Width = NullableIntAttr(child, "width"),
                        Align = ParseTextAlign(Attr(child, "align"))
                    });
                    break;
                case "row-template":
                    rowTemplate = ParseTableRow(child);
                    break;
                case "row":
                    staticRows.Add(ParseTableRow(child));
                    break;
                default:
                    throw MarkupVocabulary.UnknownElement(child, "<table>");
            }
        }

        // If columns have text content, use as header
        EmailTableRowTemplate? header = null;
        var headerCells = columns.Select(c => c).ToList();
        var columnElements = el.Elements().Where(e => e.Name.LocalName == "column").ToList();
        if (columnElements.Any(ce => !string.IsNullOrWhiteSpace(ce.Value)))
        {
            header = new EmailTableRowTemplate
            {
                Cells = columnElements.Select(ce => new EmailTableCellTemplate
                {
                    Content = ce.Value.Trim(),
                    Bold = true
                }).ToList()
            };
        }

        return new EmailTableTemplate
        {
            Columns = columns,
            Header = header,
            Rows = staticRows,
            DataSource = Attr(el, "data-source"),
            RowTemplate = rowTemplate,
            BorderColor = Attr(el, "border"),
            CellPadding = IntAttr(el, "padding", 8),
            Directives = directives
        };
    }

    private static EmailTableRowTemplate ParseTableRow(XElement el)
    {
        MarkupVocabulary.RequireKnownAttributes(el, "background");
        var cells = el.Elements()
            .Select(cell => cell.Name.LocalName == "cell"
                ? cell
                : throw MarkupVocabulary.UnknownElement(cell, $"<{el.Name.LocalName}>"))
            .Select(cell =>
            {
                MarkupVocabulary.RequireKnownAttributes(cell, "bold", "color", "align", "colspan");
                return new EmailTableCellTemplate
                {
                    Content = cell.Value,
                    Bold = BoolAttr(cell, "bold"),
                    Color = Attr(cell, "color"),
                    Align = Attr(cell, "align") is not null ? ParseTextAlign(Attr(cell, "align")) : null,
                    ColSpan = IntAttr(cell, "colspan", 1)
                };
            })
            .ToList();

        return new EmailTableRowTemplate
        {
            Cells = cells,
            BackgroundColor = Attr(el, "background")
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
    private static int? NullableIntAttr(XElement el, string name)
        => int.TryParse(el.Attribute(name)?.Value, out var v) ? v : null;
    private static bool BoolAttr(XElement el, string name)
        => el.Attribute(name)?.Value is "true" or "1";

    private static EmailPadding ParsePadding(XElement el, EmailPadding defaultPadding)
    {
        var padStr = Attr(el, "padding");
        if (padStr is null) return defaultPadding;
        if (int.TryParse(padStr, out var uniform)) return EmailPadding.All(uniform);
        return defaultPadding;
    }

    private static EmailPadding? ParseOptionalPadding(XElement el)
    {
        var padStr = Attr(el, "padding");
        if (padStr is null) return null;
        if (int.TryParse(padStr, out var uniform)) return EmailPadding.All(uniform);
        return null;
    }

    private static EmailTextAlign ParseTextAlign(string? value) => value?.ToLowerInvariant() switch
    {
        "center" => EmailTextAlign.Center,
        "right" => EmailTextAlign.Right,
        _ => EmailTextAlign.Left
    };

    private static EmailVerticalAlign ParseVerticalAlign(string? value) => value?.ToLowerInvariant() switch
    {
        "middle" => EmailVerticalAlign.Middle,
        "bottom" => EmailVerticalAlign.Bottom,
        _ => EmailVerticalAlign.Top
    };
}
