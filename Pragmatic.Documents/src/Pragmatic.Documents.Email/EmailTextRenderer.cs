using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Pragmatic.Email.Model;

namespace Pragmatic.Documents.Email;

/// <summary>
///     Renders the plain-text body of an <see cref="EmailModel" /> — the words of the mail, from the same
///     model as its HTML, so the two cannot drift.
/// </summary>
/// <remarks>
///     <para>
///         A mail that offers only HTML is shown as an attachment by some clients and scored as spam by
///         others, so every mail wants this part. Without a renderer each application wrote it again —
///         walking the model by hand, or keeping a second copy of the words that changed separately.
///     </para>
///     <para>
///         Not an <see cref="IEmailRenderer" />: that contract is HTML for mail clients, and a renderer
///         that answered it with text would satisfy the type and break the caller.
///     </para>
/// </remarks>
public static partial class EmailTextRenderer
{
    /// <summary>The text body: one block per paragraph, blank lines between them.</summary>
    public static string Render(EmailModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var blocks = new List<string>();

        foreach (var section in model.Sections)
            foreach (var column in section.Columns)
                foreach (var node in column.Content)
                    if (BlockOf(node) is { Length: > 0 } block)
                        blocks.Add(block);

        return string.Join("\n\n", blocks);
    }

    private static string? BlockOf(EmailNode node) => node switch
    {
        EmailHeadingNode heading => heading.Content,
        EmailTextNode text => text.AllowHtml ? WordsOf(text.Content) : text.Content,
        EmailHtmlNode html => WordsOf(html.Html),
        EmailButtonNode button => $"{button.Text}: {button.Href}",
        EmailImageNode image => string.IsNullOrWhiteSpace(image.Alt) ? null : image.Alt,
        EmailDividerNode => "----",
        EmailTableNode table => RowsOf(table),
        _ => null,
    };

    private static string RowsOf(EmailTableNode table)
    {
        var lines = new StringBuilder();

        if (table.Header is { } header)
            lines.Append(LineOf(header));

        foreach (var row in table.Rows)
        {
            if (lines.Length > 0)
                lines.Append('\n');
            lines.Append(LineOf(row));
        }

        return lines.ToString();
    }

    private static string LineOf(EmailTableRow row)
        => string.Join(" | ", row.Cells.Select(cell => cell.Content));

    /// <summary>The words of trusted markup: line breaks kept, tags dropped, entities decoded.</summary>
    private static string WordsOf(string markup)
    {
        var withBreaks = LineBreak().Replace(markup, "\n");
        var withoutTags = Tag().Replace(withBreaks, "");

        return WebUtility.HtmlDecode(withoutTags).Trim();
    }

    [GeneratedRegex(@"<\s*(br|/p|/div|/li|/h[1-6])\s*/?\s*>", RegexOptions.IgnoreCase)]
    private static partial Regex LineBreak();

    [GeneratedRegex(@"<[^>]*>")]
    private static partial Regex Tag();
}
