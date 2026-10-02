namespace Pragmatic.Email.Model;

/// <summary>Fluent builder for <see cref="EmailColumn"/>.</summary>
public sealed class ColumnBuilder
{
    private readonly double _width;
    private EmailVerticalAlign _verticalAlign = EmailVerticalAlign.Top;
    private EmailPadding? _padding;
    private readonly List<EmailNode> _content = [];

    internal ColumnBuilder(double width = 1.0) => _width = width;

    public ColumnBuilder VerticalAlign(EmailVerticalAlign align) { _verticalAlign = align; return this; }
    public ColumnBuilder Padding(EmailPadding padding) { _padding = padding; return this; }

    public ColumnBuilder Add(EmailNode node) { _content.Add(node); return this; }
    public ColumnBuilder Text(string content, EmailTextAlign align = EmailTextAlign.Left, string? color = null)
        => Add(new EmailTextNode { Content = content, Align = align, Color = color });
    public ColumnBuilder Heading(string content, int level = 1, EmailTextAlign align = EmailTextAlign.Left)
        => Add(new EmailHeadingNode { Content = content, Level = level, Align = align });
    public ColumnBuilder Image(string source, string alt, int? width = null, string? link = null)
        => Add(new EmailImageNode { Source = source, Alt = alt, Width = width, Link = link });
    public ColumnBuilder Button(string text, string href, string? backgroundColor = null)
        => Add(new EmailButtonNode { Text = text, Href = href, BackgroundColor = backgroundColor ?? "#007bff" });
    public ColumnBuilder Spacer(int height = 20) => Add(new EmailSpacerNode { Height = height });
    public ColumnBuilder Divider(string? color = null) => Add(new EmailDividerNode { Color = color ?? "#cccccc" });
    /// <summary>
    ///     Add a raw HTML block. Safe by default: when <paramref name="isTrusted" /> is <c>false</c>
    ///     (the default) the markup is HTML-encoded. Pass <c>true</c> only for markup you fully
    ///     control, which is then emitted verbatim without sanitization.
    /// </summary>
    public ColumnBuilder Html(string html, bool isTrusted = false)
        => Add(new EmailHtmlNode { Html = html, IsTrusted = isTrusted });

    /// <summary>Add a structured table.</summary>
    public ColumnBuilder Table(Action<EmailTableBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new EmailTableBuilder();
        configure(builder);
        return Add(builder.Build());
    }

    internal EmailColumn Build() => new()
    {
        Width = _width,
        VerticalAlign = _verticalAlign,
        Padding = _padding,
        Content = [.._content]
    };
}
