using System.Text;
using System.Text.RegularExpressions;
using Pragmatic.Email.Model;

namespace Pragmatic.Documents.Email;

/// <summary>
/// Renders <see cref="EmailModel"/> to HTML email.
/// Produces table-based layout, inline CSS, MSO/VML conditionals for Outlook compatibility.
/// </summary>
public sealed class EmailHtmlRenderer : IEmailRenderer
{
    public string Render(EmailModel model)
    {
        using var sw = new StringWriter();
        RenderTo(sw, model);
        return sw.ToString();
    }

    public void RenderTo(TextWriter w, EmailModel model)
    {
        RenderDoctype(w);
        RenderHtmlOpen(w, model);
        RenderHead(w, model);
        RenderBodyOpen(w, model);
        RenderPreheader(w, model);
        RenderContentTable(w, model);
        RenderBodyClose(w);
        w.Write("</html>");
    }

    private static void RenderDoctype(TextWriter w)
    {
        w.Write("<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd\">");
        w.WriteLine();
    }

    private static void RenderHtmlOpen(TextWriter w, EmailModel model)
    {
        w.Write("<html xmlns=\"http://www.w3.org/1999/xhtml\"");
        w.Write(" xmlns:v=\"urn:schemas-microsoft-com:vml\"");
        w.Write(" xmlns:o=\"urn:schemas-microsoft-com:office:office\"");
        if (model.Language is not null) w.Write($" lang=\"{Esc(model.Language)}\"");
        w.Write(">");
        w.WriteLine();
    }

    private static void RenderHead(TextWriter w, EmailModel model)
    {
        w.WriteLine("<head>");
        w.WriteLine("<meta http-equiv=\"Content-Type\" content=\"text/html; charset=UTF-8\" />");
        w.WriteLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\" />");
        w.WriteLine("<meta name=\"x-apple-disable-message-reformatting\" />");
        if (model.Subject is not null)
            w.WriteLine($"<title>{Esc(model.Subject)}</title>");

        // MSO reset
        w.WriteLine("<!--[if mso]>");
        w.WriteLine("<noscript><xml><o:OfficeDocumentSettings><o:PixelsPerInch>96</o:PixelsPerInch></o:OfficeDocumentSettings></xml></noscript>");
        w.WriteLine("<![endif]-->");

        // Reset styles (inline is primary, but <style> helps Apple Mail and others)
        w.WriteLine("<style type=\"text/css\">");
        w.WriteLine("body,table,td,a{-webkit-text-size-adjust:100%;-ms-text-size-adjust:100%;}");
        w.WriteLine("table,td{mso-table-lspace:0pt;mso-table-rspace:0pt;}");
        w.WriteLine("img{-ms-interpolation-mode:bicubic;border:0;height:auto;line-height:100%;outline:none;text-decoration:none;}");
        w.WriteLine("body{margin:0;padding:0;width:100%!important;height:100%!important;}");
        w.WriteLine("</style>");

        w.WriteLine("</head>");
    }

    private static void RenderBodyOpen(TextWriter w, EmailModel model)
    {
        var bgColor = SafeColor(model.WrapperBackgroundColor ?? model.BackgroundColor, "#ffffff");
        w.Write($"<body style=\"margin:0;padding:0;background-color:{bgColor};");
        w.Write($"font-family:{SafeFontFamily(model.FontFamily)};font-size:{model.FontSize}px;color:{SafeColor(model.TextColor)};\">");
        w.WriteLine();
    }

    private static void RenderPreheader(TextWriter w, EmailModel model)
    {
        if (model.Preheader is null) return;

        // Hidden preheader text — visible in email preview, not in body
        w.Write("<div style=\"display:none;font-size:1px;color:#ffffff;line-height:1px;max-height:0px;max-width:0px;opacity:0;overflow:hidden;\">");
        w.Write(Esc(model.Preheader));
        // Padding with zero-width spaces to push out any trailing content from preview
        w.Write("&#847; &#847; &#847; &#847; &#847; &#847; &#847; &#847; &#847; &#847; &#847; &#847;");
        w.Write("</div>");
        w.WriteLine();
    }

    private static void RenderContentTable(TextWriter w, EmailModel model)
    {
        // Outer wrapper table (100% width, background color)
        var wrapperBg = SafeColor(model.WrapperBackgroundColor ?? model.BackgroundColor, "#ffffff");
        w.Write($"<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" width=\"100%\" style=\"background-color:{wrapperBg};\">");
        w.Write("<tr><td align=\"center\" valign=\"top\">");
        w.WriteLine();

        // Inner content table (fixed width)
        w.Write($"<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" width=\"{model.Width}\" style=\"max-width:{model.Width}px;background-color:{SafeColor(model.BackgroundColor, "#ffffff")};\">");
        w.WriteLine();

        foreach (var section in model.Sections)
        {
            RenderSection(w, model, section);
        }

        w.Write("</table>");
        w.WriteLine();
        w.Write("</td></tr></table>");
        w.WriteLine();
    }

    private static void RenderSection(TextWriter w, EmailModel model, EmailSection section)
    {
        var bg = section.BackgroundColor is not null ? $"background-color:{SafeColor(section.BackgroundColor)};" : "";
        var pad = ToPaddingCss(section.Padding);

        w.Write($"<tr><td style=\"{bg}{pad}\">");
        w.WriteLine();

        if (section.Columns.Count == 1)
        {
            // Single column — no nested table needed
            RenderColumnContent(w, model, section.Columns[0]);
        }
        else
        {
            // Multi-column — nested table
            w.Write($"<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" width=\"100%\">");
            w.Write("<tr>");

            foreach (var column in section.Columns)
            {
                var colWidth = (int)(model.Width * column.Width);
                var vAlign = column.VerticalAlign.ToString().ToLowerInvariant();
                var colPad = column.Padding is not null ? ToPaddingCss(column.Padding) : "";

                w.Write($"<td width=\"{colWidth}\" valign=\"{vAlign}\" style=\"width:{colWidth}px;{colPad}\">");
                RenderColumnContent(w, model, column);
                w.Write("</td>");
            }

            w.Write("</tr></table>");
        }

        w.Write("</td></tr>");
        w.WriteLine();
    }

    private static void RenderColumnContent(TextWriter w, EmailModel model, EmailColumn column)
    {
        foreach (var node in column.Content)
        {
            RenderNode(w, model, node);
        }
    }

    private static void RenderNode(TextWriter w, EmailModel model, EmailNode node)
    {
        switch (node)
        {
            case EmailTextNode text:
                RenderText(w, model, text);
                break;
            case EmailHeadingNode heading:
                RenderHeading(w, model, heading);
                break;
            case EmailImageNode image:
                RenderImage(w, image);
                break;
            case EmailButtonNode button:
                RenderButton(w, button);
                break;
            case EmailSpacerNode spacer:
                RenderSpacer(w, spacer);
                break;
            case EmailDividerNode divider:
                RenderDivider(w, model, divider);
                break;
            case EmailTableNode table:
                RenderTable(w, model, table);
                break;
            case EmailHtmlNode html:
                // Trust contract: trusted HTML is emitted verbatim; untrusted content is
                // HTML-encoded so it is shown literally rather than interpreted as markup.
                w.Write(html.IsTrusted ? html.Html : Esc(html.Html));
                break;
        }
    }

    private static void RenderText(TextWriter w, EmailModel model, EmailTextNode text)
    {
        var color = SafeColor(text.Color ?? model.TextColor);
        var fontSize = text.FontSize ?? model.FontSize;
        var align = text.Align.ToString().ToLowerInvariant();
        var lineHeight = SafeLineHeight(text.LineHeight);

        w.Write($"<p style=\"margin:0;padding:0 0 10px 0;font-family:{SafeFontFamily(model.FontFamily)};");
        w.Write($"font-size:{fontSize}px;line-height:{lineHeight};color:{color};text-align:{align};\">");
        // Encode by default; only emit verbatim when the caller explicitly opts into trusted HTML.
        w.Write(text.AllowHtml ? text.Content : Esc(text.Content));
        w.Write("</p>");
        w.WriteLine();
    }

    private static void RenderHeading(TextWriter w, EmailModel model, EmailHeadingNode heading)
    {
        var level = Math.Clamp(heading.Level, 1, 6);
        var color = SafeColor(heading.Color ?? model.TextColor);
        var align = heading.Align.ToString().ToLowerInvariant();
        var fontSize = level switch
        {
            1 => 28, 2 => 24, 3 => 20, 4 => 18, 5 => 16, _ => 14
        };

        w.Write($"<h{level} style=\"margin:0;padding:0 0 10px 0;font-family:{SafeFontFamily(model.FontFamily)};");
        w.Write($"font-size:{fontSize}px;font-weight:bold;color:{color};text-align:{align};\">");
        w.Write(Esc(heading.Content));
        w.Write($"</h{level}>");
        w.WriteLine();
    }

    private static void RenderImage(TextWriter w, EmailImageNode image)
    {
        var align = image.Align.ToString().ToLowerInvariant();
        w.Write($"<div style=\"text-align:{align};padding:0 0 10px 0;\">");

        if (image.Link is not null)
            w.Write($"<a href=\"{SafeHref(image.Link)}\" target=\"_blank\">");

        w.Write($"<img src=\"{SafeImageSrc(image.Source)}\" alt=\"{Esc(image.Alt)}\"");
        if (image.Width.HasValue) w.Write($" width=\"{image.Width.Value}\"");
        if (image.Height.HasValue) w.Write($" height=\"{image.Height.Value}\"");
        w.Write($" style=\"display:block;border:0;outline:none;text-decoration:none;");
        if (image.Width.HasValue) w.Write($"width:{image.Width.Value}px;");
        w.Write("height:auto;\" />");

        if (image.Link is not null)
            w.Write("</a>");

        w.Write("</div>");
        w.WriteLine();
    }

    private static void RenderButton(TextWriter w, EmailButtonNode button)
    {
        var align = button.Align.ToString().ToLowerInvariant();
        var pad = button.Padding;

        w.Write($"<div style=\"text-align:{align};padding:10px 0;\">");
        w.WriteLine();

        // VML bulletproof button for Outlook
        w.Write("<!--[if mso]>");
        w.Write($"<v:roundrect xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:w=\"urn:schemas-microsoft-com:office:word\" ");
        w.Write($"href=\"{SafeHref(button.Href)}\" ");
        w.Write($"style=\"height:{pad.Top + pad.Bottom + button.FontSize + 4}px;v-text-anchor:middle;\" ");
        w.Write($"arcsize=\"{Math.Max(1, button.BorderRadius * 100 / 40)}%\" ");
        var btnBg = SafeColor(button.BackgroundColor, "#007bff");
        var btnText = SafeColor(button.TextColor, "#ffffff");
        w.Write($"strokecolor=\"{btnBg}\" fillcolor=\"{btnBg}\">");
        w.Write("<w:anchorlock/>");
        w.Write($"<center style=\"color:{btnText};font-family:Arial,Helvetica,sans-serif;font-size:{button.FontSize}px;font-weight:bold;\">");
        w.Write(Esc(button.Text));
        w.Write("</center>");
        w.Write("</v:roundrect>");
        w.Write("<![endif]-->");
        w.WriteLine();

        // Standard HTML button for non-Outlook
        w.Write("<!--[if !mso]><!-->");
        w.Write($"<a href=\"{SafeHref(button.Href)}\" target=\"_blank\" ");
        w.Write($"style=\"display:inline-block;background-color:{btnBg};");
        w.Write($"color:{btnText};font-family:Arial,Helvetica,sans-serif;");
        w.Write($"font-size:{button.FontSize}px;font-weight:bold;text-align:center;text-decoration:none;");
        w.Write($"border-radius:{button.BorderRadius}px;");
        w.Write($"padding:{pad.Top}px {pad.Right}px {pad.Bottom}px {pad.Left}px;");
        w.Write($"mso-padding-alt:0;\">");
        w.Write(Esc(button.Text));
        w.Write("</a>");
        w.Write("<!--<![endif]-->");
        w.WriteLine();

        w.Write("</div>");
        w.WriteLine();
    }

    private static void RenderSpacer(TextWriter w, EmailSpacerNode spacer)
    {
        w.Write($"<div style=\"height:{spacer.Height}px;line-height:{spacer.Height}px;font-size:1px;\">&#160;</div>");
        w.WriteLine();
    }

    private static void RenderDivider(TextWriter w, EmailModel model, EmailDividerNode divider)
    {
        w.Write($"<table role=\"presentation\" cellpadding=\"0\" cellspacing=\"0\" border=\"0\" width=\"100%\" style=\"padding:10px 0;\">");
        w.Write($"<tr><td style=\"border-top:{divider.Thickness}px solid {SafeColor(divider.Color, "#cccccc")};font-size:1px;line-height:1px;\">&#160;</td></tr>");
        w.Write("</table>");
        w.WriteLine();
    }

    private static string ToPaddingCss(EmailPadding padding)
        => $"padding:{padding.Top}px {padding.Right}px {padding.Bottom}px {padding.Left}px;";

    private static void RenderTable(TextWriter w, EmailModel model, EmailTableNode table)
    {
        var border = table.BorderColor is not null
            ? $"border:1px solid {SafeColor(table.BorderColor)};border-collapse:collapse;"
            : "border-collapse:collapse;";

        w.Write($"<table role=\"presentation\" cellpadding=\"{table.CellPadding}\" cellspacing=\"0\" width=\"100%\" ");
        w.Write($"style=\"{border}font-family:{SafeFontFamily(model.FontFamily)};font-size:{model.FontSize}px;color:{SafeColor(model.TextColor)};padding:0 0 10px 0;\">");
        w.WriteLine();

        // Header
        if (table.Header is not null)
        {
            RenderTableRow(w, table, table.Header, isHeader: true);
        }

        // Body rows
        foreach (var row in table.Rows)
        {
            RenderTableRow(w, table, row, isHeader: false);
        }

        w.Write("</table>");
        w.WriteLine();
    }

    private static void RenderTableRow(TextWriter w, EmailTableNode table, EmailTableRow row, bool isHeader)
    {
        var rowBg = row.BackgroundColor is not null ? $"background-color:{SafeColor(row.BackgroundColor)};" : "";
        w.Write($"<tr style=\"{rowBg}\">");

        for (var i = 0; i < row.Cells.Count; i++)
        {
            var cell = row.Cells[i];
            var col = i < table.Columns.Count ? table.Columns[i] : null;

            var align = (cell.Align ?? col?.Align ?? EmailTextAlign.Left).ToString().ToLowerInvariant();
            var cellBorder = table.BorderColor is not null ? $"border:1px solid {SafeColor(table.BorderColor)};" : "";
            var width = col?.Width is not null ? $"width:{col.Width}px;" : "";
            var color = cell.Color is not null ? $"color:{SafeColor(cell.Color)};" : "";
            var fontWeight = (cell.Bold || isHeader) ? "font-weight:bold;" : "";
            var colSpan = cell.ColSpan > 1 ? $" colspan=\"{cell.ColSpan}\"" : "";

            w.Write($"<td{colSpan} style=\"{cellBorder}{width}{color}{fontWeight}text-align:{align};padding:{table.CellPadding}px;\">");
            w.Write(Esc(cell.Content));
            w.Write("</td>");
        }

        w.Write("</tr>");
        w.WriteLine();
    }

    private static void RenderBodyClose(TextWriter w)
    {
        w.Write("</body>");
        w.WriteLine();
    }

    private static string Esc(string value)
        => System.Net.WebUtility.HtmlEncode(value);

    /// <summary>
    ///     HTML-encodes a hyperlink URL only if its scheme is safe (http/https/mailto). A
    ///     <c>javascript:</c> or <c>data:</c> URL — an XSS vector in browser-based mail clients — is
    ///     replaced by a harmless "#". Plain HTML-encoding (as before) does NOT stop scheme-based XSS.
    /// </summary>
    private static string SafeHref(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "#";
        var trimmed = url.TrimStart();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
            return Esc(trimmed);
        return "#";
    }

    /// <summary>
    ///     HTML-encodes an image source only if its scheme is safe. In addition to http/https this allows
    ///     <c>data:image/</c> (inline base64 images) and <c>cid:</c> (email attachments); anything else
    ///     (notably <c>javascript:</c>) is dropped to an empty src.
    /// </summary>
    private static string SafeImageSrc(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return "";
        var trimmed = url.TrimStart();
        if (trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("cid:", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            return Esc(trimmed);
        return "";
    }

    // Accepts: #RGB / #RGBA / #RRGGBB / #RRGGBBAA, CSS named colors ([a-z]+), and
    // rgb(…)/rgba(…)/hsl(…)/hsla(…) functional notation with numeric components.
    // Anything else (including values containing ';', ':' or parens outside the allowed
    // functional forms) is CSS injection and is replaced by the fallback.
    // \A…\z (not ^…$) so a trailing newline cannot smuggle content past the anchor.
    private static readonly Regex SafeColorRegex = new(
        @"\A(#(?:[0-9a-fA-F]{3,4}|[0-9a-fA-F]{6}|[0-9a-fA-F]{8})|[a-zA-Z]+|(?:rgba?|hsla?)\(\s*[0-9.,%\s]+\s*\))\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Font-family values must be printable, comma-separated font names. A literal double-quote (")
    // would terminate the style="…" attribute, so it is NOT allowed (single quotes for CSS-quoted
    // names are safe inside a double-quoted attribute).
    private static readonly Regex SafeFontFamilyRegex = new(
        @"\A[A-Za-z0-9\s,\-_']+\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Line-height: a bare number, a number with a CSS unit, or the keyword "normal". Anything else
    // (which could break out of the style="…" attribute) is rejected to the default.
    private static readonly Regex SafeLineHeightRegex = new(
        @"\A(normal|[0-9]+(\.[0-9]+)?(px|em|rem|%)?)\z",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    ///     Validates a user-supplied CSS color value. Returns <paramref name="fallback" /> if
    ///     the value is null, empty, or contains characters that would let an attacker break
    ///     out of the <c>style="..."</c> attribute (e.g. <c>red;content:url(…)</c>).
    /// </summary>
    private static string SafeColor(string? value, string fallback = "#000000")
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        return SafeColorRegex.IsMatch(value) ? value : fallback;
    }

    /// <summary>Validates a user-supplied font-family value against a strict allow-list.</summary>
    private static string SafeFontFamily(string? value, string fallback = "Arial, Helvetica, sans-serif")
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        return SafeFontFamilyRegex.IsMatch(value) ? value : fallback;
    }

    /// <summary>
    ///     Validates a user-supplied CSS line-height. Returns "1.5" if the value is null, empty, or
    ///     contains characters that would let an attacker break out of the <c>style="..."</c> attribute
    ///     (e.g. <c>1.5"&gt;&lt;script&gt;</c>).
    /// </summary>
    private static string SafeLineHeight(string? value, string fallback = "1.5")
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        return SafeLineHeightRegex.IsMatch(value) ? value : fallback;
    }
}
