namespace Pragmatic.Email.Model;

/// <summary>Builder for the <see cref="EmailBuilder.Article"/> component.</summary>
public sealed class ArticleBuilder
{
    internal string? ImageSource { get; private set; }
    internal string? ImageAlt { get; private set; }
    internal int? ImageWidth { get; private set; }
    internal string? HeadingText { get; private set; }
    internal int HeadingLevel { get; private set; } = 2;
    internal string? BodyText { get; private set; }
    internal string? ButtonText { get; private set; }
    internal string? ButtonHref { get; private set; }

    public ArticleBuilder Image(string source, string alt, int? width = null) { ImageSource = source; ImageAlt = alt; ImageWidth = width; return this; }
    public ArticleBuilder Heading(string text, int level = 2) { HeadingText = text; HeadingLevel = level; return this; }
    public ArticleBuilder Text(string text) { BodyText = text; return this; }
    public ArticleBuilder Button(string text, string href) { ButtonText = text; ButtonHref = href; return this; }
}
