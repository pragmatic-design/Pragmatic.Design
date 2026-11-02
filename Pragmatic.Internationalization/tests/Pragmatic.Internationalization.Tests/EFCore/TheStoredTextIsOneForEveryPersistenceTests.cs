using Pragmatic.Internationalization.EntityFrameworkCore.ValueConverters;
using Pragmatic.Internationalization.Types;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Internationalization.Tests.EFCore;

/// <summary>
///     A <see cref="LocalizedString" />'s stored text is <see cref="LocalizedString.ToJson" />'s, for EF Core's converter as for the Sql and
///     documents engines (#475): one database holds the same text whichever persistence wrote it. The text is pinned, because rows already
///     written by EF Core must read back.
/// </summary>
public class TheStoredTextIsOneForEveryPersistenceTests
{
    [Fact]
    public void EfCoresConverter_WritesTheTextLocalizedStringWrites()
    {
        var title = LocalizedString.From(("en", "Welcome"), ("fr", "Bienvenue à l'hôtel"));

        new LocalizedStringValueConverter().ConvertToProvider(title).Should().Be(title.ToJson());
    }

    /// <summary>The format EF Core has always written: escaped as System.Text.Json's default encoder escapes, keys as given.</summary>
    [Fact]
    public void TheText_IsTheJsonObjectEfCoreHasAlwaysWritten()
    {
        var title = LocalizedString.From(("en", "Welcome"), ("fr", "Bienvenue à l'hôtel"));

        title.ToJson().Should().Be("{\"en\":\"Welcome\",\"fr\":\"Bienvenue \\u00E0 l\\u0027h\\u00F4tel\"}");
    }

    [Fact]
    public void TheText_ReadsBackEveryTranslation()
    {
        var read = LocalizedString.FromJson("{\"en\":\"Welcome\",\"it\":\"Benvenuti\"}");

        read.Get("it").Should().Be("Benvenuti");
        read.Count.Should().Be(2);
        LocalizedString.FromJson(null).Count.Should().Be(0, "a null column is an empty localized string, as EF Core's converter read it");
    }
}
