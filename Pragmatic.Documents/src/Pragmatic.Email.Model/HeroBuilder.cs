namespace Pragmatic.Email.Model;

/// <summary>Builder for the <see cref="EmailBuilder.Hero"/> component.</summary>
public sealed class HeroBuilder
{
    internal string? ImageSource { get; private set; }
    internal string? ImageAlt { get; private set; }
    internal int? ImageWidth { get; private set; }
    internal int SpacerBeforeHeading { get; private set; } = 20;
    internal string? HeadingText { get; private set; }
    internal int HeadingLevel { get; private set; } = 1;
    internal string? BodyText { get; private set; }
    internal string? TextColor { get; private set; }
    internal string? ButtonText { get; private set; }
    internal string? ButtonHref { get; private set; }
    internal string? ButtonColor { get; private set; }

    public HeroBuilder Image(string source, string alt, int? width = null) { ImageSource = source; ImageAlt = alt; ImageWidth = width; return this; }
    public HeroBuilder Heading(string text, int level = 1) { HeadingText = text; HeadingLevel = level; return this; }
    public HeroBuilder Text(string text, string? color = null) { BodyText = text; TextColor = color; return this; }
    public HeroBuilder Button(string text, string href, string? backgroundColor = null) { ButtonText = text; ButtonHref = href; ButtonColor = backgroundColor; return this; }
    public HeroBuilder SpacerHeight(int height) { SpacerBeforeHeading = height; return this; }
}
