using Pragmatic.Internationalization.Types;

namespace Pragmatic.Internationalization.Tests.EFCore;

/// <summary>An entity with two <see cref="LocalizedString" /> columns, one of them optional, that no configuration declares.</summary>
public sealed class UndeclaredArticle
{
    public int Id { get; set; }

    public LocalizedString Title { get; set; } = new();

    public LocalizedString? Summary { get; set; }
}
