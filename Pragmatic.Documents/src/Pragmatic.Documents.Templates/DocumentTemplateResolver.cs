using System.Collections;
using Pragmatic.Documents.Model;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.Expressions;
using Pragmatic.Documents.Templating.Pipes;
using Pragmatic.Documents.Templates.Nodes;

namespace Pragmatic.Documents.Templates;

/// <summary>
/// Resolves a <see cref="DocumentTemplate"/> + <see cref="TemplateDataContext"/>
/// into a literal <see cref="DocumentModel"/> ready for rendering.
/// </summary>
public sealed class DocumentTemplateResolver(PipeRegistry? pipes = null)
{
    private readonly ExpressionEvaluator _evaluator = new(pipes);
    private readonly Dictionary<string, DocumentPartialTemplate> _partials = [];
    private IDocumentPartialProvider? _partialProvider;
    private readonly int _maxPartialDepth = 10;

    public DocumentTemplateResolver WithPartial(string name, DocumentPartialTemplate partial)
    {
        _partials[name] = partial;
        return this;
    }

    public DocumentTemplateResolver WithPartialProvider(IDocumentPartialProvider provider)
    {
        _partialProvider = provider;
        return this;
    }

    /// <summary>Resolve template + data → DocumentModel.</summary>
    public async ValueTask<DocumentModel> ResolveAsync(
        DocumentTemplate template,
        TemplateDataContext data,
        CancellationToken ct = default)
    {
        var title = template.Title is not null
            ? await ResolveStringAsync(template.Title, data, ct)
            : null;

        var author = template.Author is not null
            ? await ResolveStringAsync(template.Author, data, ct)
            : null;

        List<DocumentPage> pages;

        if (template.PageDataSource is not null)
        {
            // Batch: one page per item in collection
            pages = await ResolvePageBatchAsync(template, data, ct);
        }
        else
        {
            pages = [];
            foreach (var pageTemplate in template.Pages)
            {
                pages.Add(await ResolvePageAsync(pageTemplate, data, ct));
            }
        }

        return new DocumentModel
        {
            Title = title,
            Author = author,
            Language = template.Language,
            PageSize = template.PageSize,
            Orientation = template.Orientation,
            Margins = template.Margins,
            Pages = pages
        };
    }

    private async ValueTask<List<DocumentPage>> ResolvePageBatchAsync(
        DocumentTemplate template, TemplateDataContext data, CancellationToken ct)
    {
        var collection = await data.ResolveCollectionAsync(template.PageDataSource!, ct);
        if (collection is null) return [];

        var pageTemplate = template.Pages.Count > 0 ? template.Pages[0] : new DocumentPageTemplate();
        var pages = new List<DocumentPage>();

        foreach (var item in collection)
        {
            var childCtx = data.CreateChildScope(template.PageItemName, item);
            pages.Add(await ResolvePageAsync(pageTemplate, childCtx, ct));
        }

        return pages;
    }

    private async ValueTask<DocumentPage> ResolvePageAsync(
        DocumentPageTemplate pageTemplate, TemplateDataContext data, CancellationToken ct)
    {
        var header = pageTemplate.Header is not null
            ? await ResolveNodesAsync(pageTemplate.Header, data, ct)
            : null;

        var footer = pageTemplate.Footer is not null
            ? await ResolveNodesAsync(pageTemplate.Footer, data, ct)
            : null;

        var content = await ResolveNodesAsync(pageTemplate.Content, data, ct);

        return new DocumentPage
        {
            Header = header,
            Footer = footer,
            Content = content
        };
    }

    private async ValueTask<List<DocumentNode>> ResolveNodesAsync(
        IReadOnlyList<DocumentNodeTemplate> templates, TemplateDataContext data, CancellationToken ct, int partialDepth = 0)
    {
        var result = new List<DocumentNode>();

        foreach (var template in templates)
        {
            // $if directive
            if (template.Directives?.If is not null)
            {
                var condition = ExpressionParser.ParseExpression(template.Directives.If);
                if (!await _evaluator.EvaluateToBoolAsync(condition, data, ct))
                    continue;
            }

            // $for directive
            if (template.Directives?.For is not null)
            {
                var forNodes = await ResolveForDirectiveAsync(template, data, ct, partialDepth);
                result.AddRange(forNodes);
                continue;
            }

            var resolved = await ResolveNodeAsync(template, data, ct, partialDepth);
            if (resolved is not null)
                result.AddRange(resolved);
        }

        return result;
    }

    private async ValueTask<List<DocumentNode>> ResolveForDirectiveAsync(
        DocumentNodeTemplate template, TemplateDataContext data, CancellationToken ct, int partialDepth)
    {
        var forExpr = template.Directives!.For!;
        var (itemName, collectionPath) = ParseForExpression(forExpr);

        var collection = await data.ResolveCollectionAsync(collectionPath, ct);
        if (collection is null) return [];

        var nodes = new List<DocumentNode>();
        foreach (var item in collection)
        {
            var childCtx = data.CreateChildScope(itemName, item);
            // Resolve this node WITHOUT the $for directive (already expanded)
            var withoutFor = template with { Directives = template.Directives with { For = null } };
            var resolved = await ResolveNodeAsync(withoutFor, childCtx, ct, partialDepth);
            if (resolved is not null)
                nodes.AddRange(resolved);
        }

        return nodes;
    }

    private async ValueTask<IReadOnlyList<DocumentNode>?> ResolveNodeAsync(
        DocumentNodeTemplate template, TemplateDataContext data, CancellationToken ct, int partialDepth)
    {
        return template switch
        {
            TextTemplate t => [new TextNode { Content = await ResolveStringAsync(t.Content, data, ct) }],
            HeadingTemplate h => [new HeadingNode { Content = await ResolveStringAsync(h.Content, data, ct), Level = h.Level }],
            ParagraphTemplate p => [new ParagraphNode { Children = await ResolveNodesAsync(p.Children, data, ct, partialDepth) }],
            ImageTemplate img => [new ImageNode
            {
                Source = await ResolveStringAsync(img.Source, data, ct),
                Alt = img.Alt is not null ? await ResolveStringAsync(img.Alt, data, ct) : null,
                Width = img.Width, Height = img.Height
            }],
            BarcodeTemplate bc => [new BarcodeNode
            {
                Value = await ResolveStringAsync(bc.Value, data, ct),
                Type = bc.Type, Width = bc.Width, Height = bc.Height
            }],
            TableTemplate tbl => [await ResolveTableAsync(tbl, data, ct, partialDepth)],
            ListTemplate lst => [await ResolveListAsync(lst, data, ct, partialDepth)],
            ContainerTemplate c => [new ContainerNode { Children = await ResolveNodesAsync(c.Children, data, ct, partialDepth) }],
            ForEachTemplate fe => await ResolveForEachAsync(fe, data, ct, partialDepth),
            PartialTemplate pt => await ResolvePartialAsync(pt, data, ct, partialDepth),
            HorizontalRuleTemplate hr => [new HorizontalRuleNode { Thickness = hr.Thickness }],
            SpacerTemplate sp => [new SpacerNode { Height = sp.Height }],
            PageBreakTemplate => [new PageBreakNode()],
            // An unrecognized template type is a misconfiguration, not "render nothing".
            // Returning null silently would drop content with no signal; surface it loudly.
            _ => throw new TemplateParseException(
                $"Unrecognized document node template type '{template.GetType().FullName}'. " +
                "It has no resolver mapping and cannot be rendered.")
        };
    }

    private async ValueTask<TableNode> ResolveTableAsync(
        TableTemplate tbl, TemplateDataContext data, CancellationToken ct, int partialDepth)
    {
        TableRow? header = tbl.Header is not null
            ? await ResolveTableRowAsync(tbl.Header, data, ct, partialDepth)
            : null;

        var rows = new List<TableRow>();

        // A DataSource without a RowTemplate (or vice versa) is a misconfiguration: the
        // author asked for dynamic rows but gave no way to render them (or the reverse).
        // Silently falling back to static rows would render nothing — surface it instead.
        if ((tbl.DataSource is not null) != (tbl.RowTemplate is not null))
            throw new TemplateParseException(
                "Table template misconfiguration: DataSource and RowTemplate must be set together. " +
                $"DataSource={(tbl.DataSource is null ? "null" : $"'{tbl.DataSource}'")}, " +
                $"RowTemplate={(tbl.RowTemplate is null ? "null" : "set")}.");

        if (tbl.DataSource is not null && tbl.RowTemplate is not null)
        {
            // Data-bound rows
            var collection = await data.ResolveCollectionAsync(tbl.DataSource, ct);
            if (collection is not null)
            {
                foreach (var item in collection)
                {
                    var childCtx = data.CreateChildScope("item", item);
                    rows.Add(await ResolveTableRowAsync(tbl.RowTemplate, childCtx, ct, partialDepth));
                }
            }
        }
        else
        {
            // Static rows
            foreach (var row in tbl.Rows)
                rows.Add(await ResolveTableRowAsync(row, data, ct, partialDepth));
        }

        return new TableNode
        {
            Columns = tbl.Columns,
            Header = header,
            Rows = rows,
            RepeatHeader = tbl.RepeatHeader
        };
    }

    private async ValueTask<TableRow> ResolveTableRowAsync(
        TableRowTemplate rowTemplate, TemplateDataContext data, CancellationToken ct, int partialDepth)
    {
        var cells = new List<TableCell>();
        foreach (var cellTemplate in rowTemplate.Cells)
        {
            cells.Add(new TableCell
            {
                Content = await ResolveNodesAsync(cellTemplate.Content, data, ct, partialDepth),
                ColSpan = cellTemplate.ColSpan,
                RowSpan = cellTemplate.RowSpan
            });
        }
        return new TableRow { Cells = cells };
    }

    private async ValueTask<ListNode> ResolveListAsync(
        ListTemplate lst, TemplateDataContext data, CancellationToken ct, int partialDepth)
    {
        var items = new List<ListItem>();

        // Same paired-misconfiguration guard as tables: DataSource and ItemTemplate
        // must be supplied together, otherwise dynamic items render nothing silently.
        if ((lst.DataSource is not null) != (lst.ItemTemplate is not null))
            throw new TemplateParseException(
                "List template misconfiguration: DataSource and ItemTemplate must be set together. " +
                $"DataSource={(lst.DataSource is null ? "null" : $"'{lst.DataSource}'")}, " +
                $"ItemTemplate={(lst.ItemTemplate is null ? "null" : "set")}.");

        if (lst.DataSource is not null && lst.ItemTemplate is not null)
        {
            var collection = await data.ResolveCollectionAsync(lst.DataSource, ct);
            if (collection is not null)
            {
                foreach (var item in collection)
                {
                    var childCtx = data.CreateChildScope("item", item);
                    items.Add(new ListItem
                    {
                        Content = await ResolveNodesAsync(lst.ItemTemplate.Content, childCtx, ct, partialDepth)
                    });
                }
            }
        }
        else
        {
            foreach (var itemTemplate in lst.Items)
                items.Add(new ListItem
                {
                    Content = await ResolveNodesAsync(itemTemplate.Content, data, ct, partialDepth)
                });
        }

        return new ListNode { Ordered = lst.Ordered, Items = items };
    }

    private async ValueTask<IReadOnlyList<DocumentNode>> ResolveForEachAsync(
        ForEachTemplate fe, TemplateDataContext data, CancellationToken ct, int partialDepth)
    {
        var collection = await data.ResolveCollectionAsync(fe.DataSource, ct);
        if (collection is null) return [];

        var nodes = new List<DocumentNode>();
        foreach (var item in collection)
        {
            var childCtx = data.CreateChildScope(fe.ItemName, item);
            nodes.AddRange(await ResolveNodesAsync(fe.Children, childCtx, ct, partialDepth));
        }
        return nodes;
    }

    private async ValueTask<IReadOnlyList<DocumentNode>> ResolvePartialAsync(
        PartialTemplate pt, TemplateDataContext data, CancellationToken ct, int partialDepth)
    {
        if (partialDepth >= _maxPartialDepth)
            throw new InvalidOperationException($"Partial template recursion depth exceeded ({_maxPartialDepth}) for '{pt.Name}'");

        DocumentPartialTemplate? partial = null;

        if (_partials.TryGetValue(pt.Name, out var inMemory))
            partial = inMemory;
        else if (_partialProvider is not null)
            partial = await _partialProvider.GetAsync(pt.Name, ct);

        if (partial is null)
            throw new InvalidOperationException($"Partial template '{pt.Name}' not found");

        return await ResolveNodesAsync(partial.Content, data, ct, partialDepth + 1);
    }

    private async ValueTask<string> ResolveStringAsync(string template, TemplateDataContext data, CancellationToken ct)
    {
        if (!template.Contains("{{")) return template;

        var expr = ExpressionParser.ParseTemplate(template);
        return await _evaluator.EvaluateToStringAsync(expr, data, ct);
    }

    private static (string ItemName, string CollectionPath) ParseForExpression(string forExpr)
    {
        // Format: "item in collection.path"
        var parts = forExpr.Split(" in ", 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            throw new TemplateParseException($"Invalid $for expression: '{forExpr}'. Expected 'itemName in collectionPath'.");
        return (parts[0], parts[1]);
    }
}
