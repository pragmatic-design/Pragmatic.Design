using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Tests.EFCore;

/// <summary>An entity with a <see cref="LocalizedString" /> column, for the change-tracking tests.</summary>
public sealed class TranslatedArticle
{
    public int Id { get; set; }

    public LocalizedString Title { get; set; } = new();
}
