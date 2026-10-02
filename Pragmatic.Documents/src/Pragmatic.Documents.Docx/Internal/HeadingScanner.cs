using Pragmatic.Documents.Model;

namespace Pragmatic.Documents.Docx.Internal;

/// <summary>
/// Walks document nodes in the same order the renderers emit them, invoking a callback for every
/// <see cref="HeadingNode"/>. Heading bookmark IDs and TOC entries are assigned from a counter that
/// must stay in lockstep with render-time consumption; a walk that only descended into
/// <see cref="ContainerNode"/> (the old behaviour) missed headings nested in tables, lists, bookmarks,
/// paragraphs or hyperlinks — desyncing the counter and breaking those TOC entries / bookmark links.
/// </summary>
internal static class HeadingScanner
{
    internal static void Walk(IEnumerable<DocumentNode> nodes, Action<HeadingNode> onHeading)
    {
        foreach (var node in nodes)
            WalkNode(node, onHeading);
    }

    private static void WalkNode(DocumentNode node, Action<HeadingNode> onHeading)
    {
        switch (node)
        {
            case HeadingNode h:
                onHeading(h);
                break;
            case ContainerNode c:
                Walk(c.Children, onHeading);
                break;
            case ParagraphNode p:
                Walk(p.Children, onHeading);
                break;
            case BookmarkNode b:
                Walk(b.Children, onHeading);
                break;
            case HyperlinkNode hl:
                Walk(hl.Children, onHeading);
                break;
            case TableNode t:
                // Header first (rendered once), then body rows — matching TableNodeRenderer.
                if (t.Header is not null)
                    foreach (var cell in t.Header.Cells)
                        Walk(cell.Content, onHeading);
                foreach (var row in t.Rows)
                    foreach (var cell in row.Cells)
                        Walk(cell.Content, onHeading);
                break;
            case ListNode l:
                foreach (var item in l.Items)
                {
                    Walk(item.Content, onHeading);
                    if (item.SubList is not null)
                        WalkNode(item.SubList, onHeading);
                }
                break;
        }
    }
}
