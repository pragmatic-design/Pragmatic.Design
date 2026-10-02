using Pragmatic.Internationalization.Providers;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Providers;

/// <summary>
///     A culture with a region resolves through the file named for its language.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ The miss is silent. A <c>GetCultureData</c> that looked for <c>{culture}.json</c> and
///         stopped would leave a host configured with <c>en-US</c> and shipping <c>en.json</c> localizing
///         <b>nothing</b> — <c>GetString</c> returns null, the error resolver returns null, and the
///         ProblemDetails factory falls back to the error's own English text. Everything keeps working,
///         in the wrong language, with no log above Debug.
///     </para>
///     <para>
///         These cases read the provider directly: an assertion on an error whose <c>Title</c> is the
///         same string as its English translation passes whether the lookup resolved or not.
///     </para>
///     <para>
///         .NET's own <c>ResourceManager</c> falls back the same way — culture, then language, then the
///         neutral resource — so a provider that does not is surprising in the one direction that costs
///         nothing to be wrong in.
///     </para>
/// </remarks>
public sealed class ACultureWithARegionTests : IDisposable
{
    private readonly DirectoryInfo _translations = Directory.CreateTempSubdirectory("pragmatic-i18n-");

    private void Write(string culture, string json)
        => File.WriteAllText(Path.Combine(_translations.FullName, $"{culture}.json"), json);

    private JsonLocalizationProvider Provider() => new(_translations.FullName);

    public void Dispose() => _translations.Delete(recursive: true);

    [Fact]
    public void ACultureWithARegion_ReadsTheFileNamedForItsLanguage()
    {
        Write("en", """{"greeting":"Hello"}""");

        Provider().GetString("greeting", "en-US").Should().Be("Hello",
            "a host configured with en-US and shipping en.json localizes nothing otherwise");
    }

    /// <summary>
    ///     The control: a language nobody ships still resolves nothing.
    /// </summary>
    /// <remarks>
    ///     Without it, "en-US finds en.json" is satisfied by a provider that answers every culture from
    ///     whichever file it happens to hold, which would make a missing translation indistinguishable
    ///     from a wrong one.
    /// </remarks>
    [Fact]
    public void ACultureWhoseLanguageHasNoFile_StillResolvesNothing()
    {
        Write("en", """{"greeting":"Hello"}""");

        Provider().GetString("greeting", "fr-FR").Should().BeNull();
    }

    /// <summary>
    ///     The exact file wins: the language file is a fallback, not a replacement.
    /// </summary>
    /// <remarks>
    ///     An application that ships both is saying the region differs, and a fallback that overrode it
    ///     would quietly discard the more specific translation it was asked for.
    /// </remarks>
    [Fact]
    public void AFileForTheExactCulture_WinsOverTheLanguageOne()
    {
        Write("en", """{"greeting":"Hello"}""");
        Write("en-US", """{"greeting":"Howdy"}""");

        Provider().GetString("greeting", "en-US").Should().Be("Howdy");
        Provider().GetString("greeting", "en-GB").Should().Be("Hello",
            "and a region with no file of its own still falls back to the language");
    }

    /// <summary>
    ///     A plural form falls back the same way, because it is read through the same lookup.
    /// </summary>
    /// <remarks>
    ///     Asserted rather than assumed: the two reads are separate methods over the same cached data,
    ///     and a fix applied to one of them would leave the other exactly as broken.
    /// </remarks>
    [Fact]
    public void APluralForm_FallsBackTheSameWay()
    {
        Write("en", """{"items":{"one":"1 item","other":"{count} items"}}""");

        Provider().GetPlural("items", "en-US").Should().NotBeNull();
    }

    /// <summary>
    ///     A malformed culture token is still refused before it reaches the filesystem.
    /// </summary>
    /// <remarks>
    ///     The fallback splits on <c>-</c>, and the first thing it must not do is turn a rejected token
    ///     into a second attempt with a different shape. <c>../secrets</c> has no <c>-</c>, so this is
    ///     the case that could regress unnoticed.
    /// </remarks>
    [Fact]
    public void AMalformedCultureToken_IsStillRefused()
    {
        Write("en", """{"greeting":"Hello"}""");

        Provider().GetString("greeting", "../en").Should().BeNull();
    }
}
