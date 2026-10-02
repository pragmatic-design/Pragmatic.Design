using System.Collections;
using System.Collections.Concurrent;
using Pragmatic.Documents.Templating.Data;
using Pragmatic.Documents.Templating.Expressions;
using Pragmatic.Documents.Templating.Pipes;
using Pragmatic.Email.Model;
using Pragmatic.Email.Templates.Nodes;

namespace Pragmatic.Email.Templates;

/// <summary>
/// Resolves an <see cref="EmailTemplate"/> + <see cref="TemplateDataContext"/>
/// into a literal <see cref="EmailModel"/> ready for rendering.
/// </summary>
public sealed class EmailTemplateResolver(PipeRegistry? pipes = null)
{
    private readonly ExpressionEvaluator _evaluator = new(pipes);
    private readonly ConcurrentDictionary<string, EmailPartialDefinition> _partials = new();
    private IEmailPartialProvider? _partialProvider;
    private readonly int _maxPartialDepth = 10;

    public EmailTemplateResolver WithPartial(string name, EmailPartialDefinition partial)
    {
        _partials[name] = partial;
        return this;
    }

    public EmailTemplateResolver WithPartialProvider(IEmailPartialProvider provider)
    {
        _partialProvider = provider;
        return this;
    }

    public async ValueTask<EmailModel> ResolveAsync(
        EmailTemplate template,
        TemplateDataContext data,
        CancellationToken ct = default)
    {
        var subject = template.Subject is not null
            ? await ResolveStringAsync(template.Subject, data, ct) : null;
        var preheader = template.Preheader is not null
            ? await ResolveStringAsync(template.Preheader, data, ct) : null;

        var sections = await ResolveSectionsAsync(template.Sections, data, ct);

        return new EmailModel
        {
            Subject = subject,
            Preheader = preheader,
            Language = template.Language,
            Width = template.Width,
            BackgroundColor = template.BackgroundColor,
            WrapperBackgroundColor = template.WrapperBackgroundColor,
            FontFamily = template.FontFamily,
            FontSize = template.FontSize,
            TextColor = template.TextColor,
            Sections = sections
        };
    }

    private async ValueTask<List<EmailSection>> ResolveSectionsAsync(
        IReadOnlyList<EmailSectionTemplate> templates, TemplateDataContext data, CancellationToken ct, int partialDepth = 0)
    {
        var result = new List<EmailSection>();

        foreach (var sectionTemplate in templates)
        {
            // $if on section
            if (sectionTemplate.Directives?.If is not null)
            {
                var cond = ExpressionParser.ParseExpression(sectionTemplate.Directives.If);
                if (!await _evaluator.EvaluateToBoolAsync(cond, data, ct))
                    continue;
            }

            // $for on section
            if (sectionTemplate.Directives?.For is not null)
            {
                var (itemName, collPath) = ParseFor(sectionTemplate.Directives.For);
                var coll = await data.ResolveCollectionAsync(collPath, ct);
                if (coll is not null)
                {
                    foreach (var item in coll)
                    {
                        var childCtx = data.CreateChildScope(itemName, item);
                        result.Add(await ResolveSectionAsync(sectionTemplate, childCtx, ct, partialDepth));
                    }
                }
                continue;
            }

            result.Add(await ResolveSectionAsync(sectionTemplate, data, ct, partialDepth));
        }

        return result;
    }

    private async ValueTask<EmailSection> ResolveSectionAsync(
        EmailSectionTemplate template, TemplateDataContext data, CancellationToken ct, int partialDepth = 0)
    {
        var columns = new List<EmailColumn>();
        foreach (var colTemplate in template.Columns)
        {
            columns.Add(new EmailColumn
            {
                Width = colTemplate.Width,
                VerticalAlign = colTemplate.VerticalAlign,
                Padding = colTemplate.Padding,
                Content = await ResolveNodesAsync(colTemplate.Content, data, ct, partialDepth)
            });
        }

        return new EmailSection
        {
            BackgroundColor = template.BackgroundColor,
            Padding = template.Padding,
            Columns = columns
        };
    }

    private async ValueTask<List<EmailNode>> ResolveNodesAsync(
        IReadOnlyList<EmailNodeTemplate> templates, TemplateDataContext data, CancellationToken ct, int partialDepth = 0)
    {
        var result = new List<EmailNode>();

        foreach (var template in templates)
        {
            if (template.Directives?.If is not null)
            {
                var cond = ExpressionParser.ParseExpression(template.Directives.If);
                if (!await _evaluator.EvaluateToBoolAsync(cond, data, ct))
                    continue;
            }

            if (template.Directives?.For is not null)
            {
                var (itemName, collPath) = ParseFor(template.Directives.For);
                var coll = await data.ResolveCollectionAsync(collPath, ct);
                if (coll is not null)
                {
                    foreach (var item in coll)
                    {
                        var childCtx = data.CreateChildScope(itemName, item);
                        var withoutFor = template with { Directives = template.Directives with { For = null } };
                        var resolved = await ResolveNodeAsync(withoutFor, childCtx, ct, partialDepth);
                        if (resolved is not null) result.AddRange(resolved);
                    }
                }
                continue;
            }

            var nodes = await ResolveNodeAsync(template, data, ct, partialDepth);
            if (nodes is not null) result.AddRange(nodes);
        }

        return result;
    }

    private async ValueTask<IReadOnlyList<EmailNode>?> ResolveNodeAsync(
        EmailNodeTemplate template, TemplateDataContext data, CancellationToken ct, int partialDepth)
    {
        return template switch
        {
            EmailTextTemplate t => [new EmailTextNode
            {
                Content = await ResolveStringAsync(t.Content, data, ct),
                FontSize = t.FontSize, Color = t.Color, Align = t.Align
            }],
            EmailHeadingTemplate h => [new EmailHeadingNode
            {
                Content = await ResolveStringAsync(h.Content, data, ct),
                Level = h.Level, Color = h.Color, Align = h.Align
            }],
            EmailImageTemplate img => [new EmailImageNode
            {
                Source = await ResolveStringAsync(img.Source, data, ct),
                Alt = await ResolveStringAsync(img.Alt, data, ct),
                Width = img.Width, Height = img.Height,
                Link = img.Link is not null ? await ResolveStringAsync(img.Link, data, ct) : null,
                Align = img.Align
            }],
            EmailButtonTemplate btn => [new EmailButtonNode
            {
                Text = await ResolveStringAsync(btn.Text, data, ct),
                Href = await ResolveStringAsync(btn.Href, data, ct),
                BackgroundColor = btn.BackgroundColor, TextColor = btn.TextColor,
                BorderRadius = btn.BorderRadius, FontSize = btn.FontSize, Align = btn.Align
            }],
            EmailTableTemplate tbl => [await ResolveTableAsync(tbl, data, ct)],
            EmailSpacerTemplate sp => [new EmailSpacerNode { Height = sp.Height }],
            EmailDividerTemplate dv => [new EmailDividerNode { Color = dv.Color, Thickness = dv.Thickness }],
            EmailHtmlTemplate html => [new EmailHtmlNode { Html = await ResolveStringAsync(html.Html, data, ct), IsTrusted = html.IsTrusted }],
            EmailPartialTemplate pt => await ResolvePartialAsync(pt, data, ct, partialDepth),
            // Fail loudly on an unrecognized node template rather than silently dropping content
            // (consistent with the document template resolver).
            _ => throw new Pragmatic.Documents.Templating.Expressions.TemplateParseException(
                $"Unknown email node template type '{template.GetType().Name}'.")
        };
    }

    private async ValueTask<EmailTableNode> ResolveTableAsync(
        EmailTableTemplate tbl, TemplateDataContext data, CancellationToken ct)
    {
        EmailTableRow? header = tbl.Header is not null
            ? await ResolveTableRowAsync(tbl.Header, data, ct) : null;

        var rows = new List<EmailTableRow>();

        // DataSource and RowTemplate must be set together — one without the other is a template error
        // (silently falling back to static rows would hide the misconfiguration). Matches the document resolver.
        if (tbl.DataSource is not null != (tbl.RowTemplate is not null))
            throw new Pragmatic.Documents.Templating.Expressions.TemplateParseException(
                "Email table must set both DataSource and RowTemplate together, or neither.");

        if (tbl.DataSource is not null && tbl.RowTemplate is not null)
        {
            var coll = await data.ResolveCollectionAsync(tbl.DataSource, ct);
            if (coll is not null)
            {
                foreach (var item in coll)
                {
                    var childCtx = data.CreateChildScope("item", item);
                    rows.Add(await ResolveTableRowAsync(tbl.RowTemplate, childCtx, ct));
                }
            }
        }
        else
        {
            foreach (var row in tbl.Rows)
                rows.Add(await ResolveTableRowAsync(row, data, ct));
        }

        return new EmailTableNode
        {
            Columns = tbl.Columns,
            Header = header,
            Rows = rows,
            BorderColor = tbl.BorderColor,
            CellPadding = tbl.CellPadding
        };
    }

    private async ValueTask<EmailTableRow> ResolveTableRowAsync(
        EmailTableRowTemplate rowTemplate, TemplateDataContext data, CancellationToken ct)
    {
        var cells = new List<EmailTableCell>();
        foreach (var cell in rowTemplate.Cells)
        {
            cells.Add(new EmailTableCell
            {
                Content = await ResolveStringAsync(cell.Content, data, ct),
                ColSpan = cell.ColSpan,
                Bold = cell.Bold,
                Color = cell.Color,
                Align = cell.Align
            });
        }
        return new EmailTableRow { Cells = cells, BackgroundColor = rowTemplate.BackgroundColor };
    }

    private async ValueTask<IReadOnlyList<EmailNode>> ResolvePartialAsync(
        EmailPartialTemplate pt, TemplateDataContext data, CancellationToken ct, int partialDepth)
    {
        if (partialDepth >= _maxPartialDepth)
            throw new InvalidOperationException($"Partial recursion depth exceeded for '{pt.Name}'");

        EmailPartialDefinition? partial = null;
        if (_partials.TryGetValue(pt.Name, out var inMemory)) partial = inMemory;
        else if (_partialProvider is not null) partial = await _partialProvider.GetAsync(pt.Name, ct);

        if (partial is null)
            throw new InvalidOperationException($"Email partial '{pt.Name}' not found");

        // Partials contain sections → flatten into nodes. Propagate the incremented depth so
        // nested partials (including cycles) are bounded by _maxPartialDepth.
        var childDepth = partialDepth + 1;
        var nodes = new List<EmailNode>();
        foreach (var section in partial.Sections)
        {
            var resolved = await ResolveSectionAsync(section, data, ct, childDepth);
            foreach (var col in resolved.Columns)
                nodes.AddRange(col.Content);
        }
        return nodes;
    }

    private async ValueTask<string> ResolveStringAsync(string template, TemplateDataContext data, CancellationToken ct)
    {
        if (!template.Contains("{{")) return template;
        var expr = ExpressionParser.ParseTemplate(template);
        return await _evaluator.EvaluateToStringAsync(expr, data, ct);
    }

    private static (string ItemName, string CollectionPath) ParseFor(string forExpr)
    {
        var parts = forExpr.Split(" in ", 2, StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
            throw new Pragmatic.Documents.Templating.Expressions.TemplateParseException(
                $"Invalid $for: '{forExpr}'. Expected 'itemName in collectionPath'.");
        return (parts[0], parts[1]);
    }
}
