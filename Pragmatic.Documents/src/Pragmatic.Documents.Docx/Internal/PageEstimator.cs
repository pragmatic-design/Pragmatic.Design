using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Internal;

/// <summary>
/// Heuristic page number estimator for TOC pre-population.
/// Two-pass: first counts headings to know TOC size, then estimates page numbers.
/// </summary>
internal static class PageEstimator
{
    internal record struct EstimationResult(Dictionary<int, int> PageNumbers, int TocTitlePage);

    internal static EstimationResult EstimatePageNumbers(DocumentModel model)
    {
        var headingsByMaxLevel = CountHeadingsByLevel(model);

        var result = new Dictionary<int, int>();
        var tocTitlePage = 1;

        var margins = model.Margins;
        var (_, pageHeightTwips) = Units.PageSizeToTwips(model.PageSize, model.Orientation);
        var pageHeightMm = pageHeightTwips / 56.6929;
        var usableHeightMm = pageHeightMm - margins.Top - margins.Bottom - 25;
        var lineHeightMm = 5.5;
        var linesPerPage = (int)Math.Floor(usableHeightMm / lineHeightMm);
        if (linesPerPage < 10) linesPerPage = 40;

        var currentLine = 0;
        var currentPage = 1;
        var headingIndex = 0;

        foreach (var page in model.Pages)
        {
            if (page != model.Pages[0])
            {
                currentPage++;
                currentLine = 0;
            }

            foreach (var node in page.Content)
                WalkNode(node, ref currentLine, ref currentPage, linesPerPage,
                    ref headingIndex, result, headingsByMaxLevel, ref tocTitlePage);
        }

        return new EstimationResult(result, tocTitlePage);
    }

    private static Dictionary<int, int> CountHeadingsByLevel(DocumentModel model)
    {
        var allHeadings = new List<int>();
        foreach (var page in model.Pages)
            CollectHeadingLevels(page.Content, allHeadings);

        var result = new Dictionary<int, int>();
        for (var maxLevel = 1; maxLevel <= 6; maxLevel++)
            result[maxLevel] = allHeadings.Count(l => l <= maxLevel);

        return result;
    }

    private static void CollectHeadingLevels(IEnumerable<DocumentNode> nodes, List<int> levels)
        // Same render-order walk as the bookmark pre-scan, so estimated TOC entry counts match.
        => HeadingScanner.Walk(nodes, h => levels.Add(h.Level));

    private static void WalkNode(
        DocumentNode node,
        ref int currentLine, ref int currentPage, int linesPerPage,
        ref int headingIndex, Dictionary<int, int> result,
        Dictionary<int, int> headingsByMaxLevel, ref int tocTitlePage)
    {
        var lines = EstimateNodeLines(node, headingsByMaxLevel);

        if (currentLine + lines > linesPerPage)
        {
            currentPage++;
            currentLine = 0;
        }

        // Track the page where the TOC title appears
        if (node is TocNode)
            tocTitlePage = currentPage;

        if (node is HeadingNode)
        {
            result[headingIndex] = currentPage;
            headingIndex++;
        }

        if (node is PageBreakNode)
        {
            // Advance to the next page boundary. Only count the break as a new page when the
            // current page already holds content; a break at the very top of a page does not
            // produce an extra blank page, so leaving currentLine at 0 keeps the estimate aligned.
            if (currentLine > 0)
            {
                currentPage++;
                currentLine = 0;
            }
            return;
        }

        currentLine += lines;

        if (node is ContainerNode container)
        {
            foreach (var child in container.Children)
                WalkNode(child, ref currentLine, ref currentPage, linesPerPage,
                    ref headingIndex, result, headingsByMaxLevel, ref tocTitlePage);
        }
    }

    private static int EstimateNodeLines(DocumentNode node, Dictionary<int, int> headingsByMaxLevel) => node switch
    {
        HeadingNode h => h.Level <= 2 ? 3 : 2,
        TextNode => 1,
        ParagraphNode p => Math.Max(1, p.Children.Count),
        SpacerNode s => Math.Max(1, (int)Math.Ceiling(s.Height / 5.5)),
        HorizontalRuleNode => 1,
        PageBreakNode => 0,
        TableNode t => 1 + (t.Header is not null ? 1 : 0) + t.Rows.Count,
        ListNode l => CountListItems(l),
        ImageNode img => Math.Max(2, (int)Math.Ceiling((img.Height ?? 75) / 5.5)),
        TocNode toc => EstimateTocLines(toc, headingsByMaxLevel),
        ContainerNode => 0,
        FootnoteNode => 0,
        FieldNode => 0,
        HyperlinkNode => 1,
        BookmarkNode b => Math.Max(1, b.Children.Count),
        BarcodeNode => 6,
        _ => 1
    };

    private static int EstimateTocLines(TocNode toc, Dictionary<int, int> headingsByMaxLevel)
    {
        var entryCount = headingsByMaxLevel.GetValueOrDefault(toc.MaxLevel, 0);
        var titleLines = toc.Title is not null ? 3 : 0;
        // +1 for the TOC title entry itself in the TOC list, +1 trailing spacing
        var selfEntry = toc.Title is not null ? 1 : 0;
        return titleLines + entryCount + selfEntry + 1;
    }

    private static int CountListItems(ListNode list)
    {
        var count = 0;
        foreach (var item in list.Items)
        {
            count++;
            if (item.SubList is not null)
                count += CountListItems(item.SubList);
        }
        return count;
    }
}
