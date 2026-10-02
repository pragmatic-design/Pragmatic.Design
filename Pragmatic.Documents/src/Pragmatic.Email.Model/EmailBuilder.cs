namespace Pragmatic.Email.Model;

/// <summary>
/// Fluent builder for constructing <see cref="EmailModel"/> instances.
/// </summary>
public sealed class EmailBuilder
{
    private string? _subject;
    private string? _preheader;
    private string? _language;
    private int _width = 600;
    private string _backgroundColor = "#ffffff";
    private string? _wrapperBackgroundColor;
    private string _fontFamily = "Arial, Helvetica, sans-serif";
    private int _fontSize = 16;
    private string _textColor = "#333333";
    private readonly List<EmailSection> _sections = [];

    public EmailBuilder Subject(string subject) { _subject = subject; return this; }
    public EmailBuilder Preheader(string preheader) { _preheader = preheader; return this; }
    public EmailBuilder Language(string language) { _language = language; return this; }
    public EmailBuilder Width(int width) { _width = width; return this; }
    public EmailBuilder BackgroundColor(string color) { _backgroundColor = color; return this; }
    public EmailBuilder WrapperBackgroundColor(string color) { _wrapperBackgroundColor = color; return this; }
    public EmailBuilder FontFamily(string fontFamily) { _fontFamily = fontFamily; return this; }
    public EmailBuilder FontSize(int fontSize) { _fontSize = fontSize; return this; }
    public EmailBuilder TextColor(string color) { _textColor = color; return this; }

    /// <summary>Add a section using a section builder.</summary>
    public EmailBuilder Section(Action<SectionBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new SectionBuilder();
        configure(builder);
        _sections.Add(builder.Build());
        return this;
    }

    /// <summary>Add a full-width section with a single column.</summary>
    public EmailBuilder FullWidthSection(Action<ColumnBuilder> configure, string? backgroundColor = null)
    {
        return Section(s =>
        {
            if (backgroundColor is not null) s.BackgroundColor(backgroundColor);
            s.Column(configure);
        });
    }

    // --- High-level components (compile to Section/Column/Node) ---

    /// <summary>Hero section — full-width with optional background, heading, text, CTA.</summary>
    public EmailBuilder Hero(Action<HeroBuilder> configure, string? backgroundColor = null)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var hero = new HeroBuilder();
        configure(hero);
        return FullWidthSection(c =>
        {
            if (hero.ImageSource is not null)
                c.Image(hero.ImageSource, hero.ImageAlt ?? "", hero.ImageWidth);
            if (hero.SpacerBeforeHeading > 0)
                c.Spacer(hero.SpacerBeforeHeading);
            if (hero.HeadingText is not null)
                c.Heading(hero.HeadingText, hero.HeadingLevel, EmailTextAlign.Center);
            if (hero.BodyText is not null)
                c.Text(hero.BodyText, EmailTextAlign.Center, hero.TextColor);
            if (hero.ButtonText is not null && hero.ButtonHref is not null)
            {
                c.Spacer(10);
                c.Button(hero.ButtonText, hero.ButtonHref, hero.ButtonColor);
            }
        }, backgroundColor);
    }

    /// <summary>Two-column layout (50/50 or custom widths).</summary>
    public EmailBuilder TwoColumns(
        Action<ColumnBuilder> left,
        Action<ColumnBuilder> right,
        double leftWidth = 0.5,
        string? backgroundColor = null)
    {
        return Section(s =>
        {
            if (backgroundColor is not null) s.BackgroundColor(backgroundColor);
            s.Column(left, leftWidth);
            s.Column(right, 1.0 - leftWidth);
        });
    }

    /// <summary>Three-column layout (equal widths by default).</summary>
    public EmailBuilder ThreeColumns(
        Action<ColumnBuilder> col1,
        Action<ColumnBuilder> col2,
        Action<ColumnBuilder> col3,
        string? backgroundColor = null)
    {
        return Section(s =>
        {
            if (backgroundColor is not null) s.BackgroundColor(backgroundColor);
            s.Column(col1, 1.0 / 3);
            s.Column(col2, 1.0 / 3);
            s.Column(col3, 1.0 / 3);
        });
    }

    /// <summary>Article component — image + heading + text + optional CTA.</summary>
    public EmailBuilder Article(Action<ArticleBuilder> configure, string? backgroundColor = null)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var article = new ArticleBuilder();
        configure(article);
        return FullWidthSection(c =>
        {
            if (article.ImageSource is not null)
                c.Image(article.ImageSource, article.ImageAlt ?? "", article.ImageWidth);
            if (article.HeadingText is not null)
                c.Heading(article.HeadingText, article.HeadingLevel);
            if (article.BodyText is not null)
                c.Text(article.BodyText);
            if (article.ButtonText is not null && article.ButtonHref is not null)
                c.Button(article.ButtonText, article.ButtonHref);
        }, backgroundColor);
    }

    /// <summary>Footer section — small centered text, ideal for unsubscribe/legal.</summary>
    public EmailBuilder Footer(Action<ColumnBuilder> configure, string? backgroundColor = null)
    {
        return Section(s =>
        {
            if (backgroundColor is not null) s.BackgroundColor(backgroundColor);
            s.Padding(EmailPadding.All(10));
            s.Column(configure);
        });
    }

    /// <summary>Social bar — centered row of image links for social icons.</summary>
    public EmailBuilder SocialBar(IEnumerable<SocialLink> links, string? backgroundColor = null)
    {
        return FullWidthSection(c =>
        {
            // Render as inline images with links
            var html = string.Join("&nbsp;&nbsp;",
                links.Select(l =>
                    $"<a href=\"{System.Net.WebUtility.HtmlEncode(l.Href)}\" target=\"_blank\" style=\"text-decoration:none;\">" +
                    $"<img src=\"{System.Net.WebUtility.HtmlEncode(l.IconUrl)}\" alt=\"{System.Net.WebUtility.HtmlEncode(l.Label)}\" " +
                    $"width=\"{l.IconSize}\" height=\"{l.IconSize}\" style=\"display:inline-block;border:0;\" />" +
                    "</a>"));
            // Internally constructed markup with pre-encoded values → explicitly trusted.
            c.Html($"<div style=\"text-align:center;padding:10px 0;\">{html}</div>", isTrusted: true);
        }, backgroundColor);
    }

    public EmailModel Build() => new()
    {
        Subject = _subject,
        Preheader = _preheader,
        Language = _language,
        Width = _width,
        BackgroundColor = _backgroundColor,
        WrapperBackgroundColor = _wrapperBackgroundColor,
        FontFamily = _fontFamily,
        FontSize = _fontSize,
        TextColor = _textColor,
        // Defensive copy: the returned model must not alias the builder's mutable backing list.
        Sections = [.._sections]
    };
}
