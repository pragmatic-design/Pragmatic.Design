using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Internationalization.AspNetCore.Extensions;
using Pragmatic.Internationalization.Providers;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Providers;

/// <summary>
///     An application asks the container for a localizer and reads every translation source it
///     registered, not the last one.
/// </summary>
/// <remarks>
///     <para>
///         Two registrations were missing and one of them had a trap in it. Nothing registered
///         <c>IStringLocalizer</c>, so a host using <c>Pragmatic.Documents.Templating.I18N</c> — whose
///         only entry point takes one — failed to start naming a type its author never wrote. And
///         writing that registration the obvious way, over
///         <c>GetRequiredService&lt;ILocalizationProvider&gt;()</c>, resolved the <em>last</em>
///         provider registered: an application with three JSON directories read one of them.
///     </para>
///     <para>
///         ⚠️ That second one is the reason this is a test and not a line in a README. Reading one
///         source out of three does not fail — it returns the key unchanged, which is exactly what a
///         missing translation returns, and a missing translation is the ordinary case every
///         application expects to see. The wrong answer is indistinguishable from a legitimate one.
///     </para>
/// </remarks>
public sealed class TheLocalizerAnswersFromEverySourceTests : IDisposable
{
    private readonly DirectoryInfo _first = Directory.CreateTempSubdirectory("pragmatic-i18n-first-");
    private readonly DirectoryInfo _second = Directory.CreateTempSubdirectory("pragmatic-i18n-second-");

    public TheLocalizerAnswersFromEverySourceTests()
    {
        Write(_first, """{"only.in.the.first":"From the first"}""");
        Write(_second, """{"only.in.the.second":"From the second"}""");
    }

    private static void Write(DirectoryInfo directory, string json)
        => File.WriteAllText(Path.Combine(directory.FullName, "en.json"), json);

    public void Dispose()
    {
        _first.Delete(recursive: true);
        _second.Delete(recursive: true);
    }

    /// <summary>
    ///     An application with two translation directories, built the way the documentation says.
    /// </summary>
    /// <remarks>
    ///     Two and not one: a single source is satisfied by any resolution at all, including the one
    ///     that picks whichever provider happens to be last.
    /// </remarks>
    private ServiceProvider AnApplicationWithTwoSources()
    {
        var services = new ServiceCollection();
        services.AddPragmaticInternationalization()
            .AddJsonTranslations(_first.FullName)
            .AddJsonTranslations(_second.FullName);

        return services.BuildServiceProvider();
    }

    /// <summary>
    ///     The setpoint: the container hands out a localizer, and it reads the source that is not last.
    /// </summary>
    [Fact]
    public void TheLocalizerIsResolvable_AndReadsTheFirstSource()
    {
        using var application = AnApplicationWithTwoSources();

        var localizer = application.GetRequiredService<IStringLocalizer>().WithCulture("en");

        localizer["only.in.the.first"].Value.Should().Be("From the first",
            "the localizer reads every registered source, not the one that happened to be registered last");
    }

    /// <summary>
    ///     ⚠️ The first control: the other source resolves too.
    /// </summary>
    /// <remarks>
    ///     Without it, "the composite is used" is satisfied by having picked the other single provider —
    ///     the same defect with the sources swapped.
    /// </remarks>
    [Fact]
    public void TheLocalizer_AlsoReadsTheSecondSource()
    {
        using var application = AnApplicationWithTwoSources();

        var localizer = application.GetRequiredService<IStringLocalizer>().WithCulture("en");

        localizer["only.in.the.second"].Value.Should().Be("From the second",
            "both directions have to hold, or one provider is still answering for all of them");
    }

    /// <summary>
    ///     ⚠️ The second control: a key nobody translated comes back as itself.
    /// </summary>
    /// <remarks>
    ///     That is the existing contract for a missing translation and it must not become an exception:
    ///     an application ships a key before it ships the sentence, and the default text is what it
    ///     reads in the meantime.
    /// </remarks>
    [Fact]
    public void AKeyNoSourceDefines_ComesBackAsItself()
    {
        using var application = AnApplicationWithTwoSources();

        var localizer = application.GetRequiredService<IStringLocalizer>().WithCulture("en");

        var result = localizer["in.neither.source"];

        result.IsMissing.Should().BeTrue();
        result.Value.Should().Be("in.neither.source",
            "a missing translation reads as its key, which is the contract every caller already relies on");
    }

    /// <summary>
    ///     Asking the container for the interface answers from every source as well.
    /// </summary>
    /// <remarks>
    ///     <c>GetRequiredService&lt;ILocalizationProvider&gt;()</c> is the obvious thing to write, and
    ///     on its own the container returns whichever provider was registered last, which yields a document
    ///     in which every key reads as its own name.
    /// </remarks>
    [Fact]
    public void AskingForTheInterface_AnswersFromEverySource()
    {
        using var application = AnApplicationWithTwoSources();

        var provider = application.GetRequiredService<ILocalizationProvider>();

        provider.GetString("only.in.the.first", "en").Should().Be("From the first");
        provider.GetString("only.in.the.second", "en").Should().Be("From the second",
            "one answer proves nothing here: the defect was a single source answering for all of them");
    }

    /// <summary>
    ///     ⚠️ A guard, not a measurement: it checks that the filter holds, not that lookups reach every
    ///     source.
    /// </summary>
    /// <remarks>
    ///     The interface answers from every source because a gateway is registered under it, and the
    ///     gateway resolves the composite lazily. The composite collects
    ///     <c>GetServices&lt;ILocalizationProvider&gt;()</c>, so it would otherwise collect the gateway
    ///     too — and every lookup would descend into itself until the stack ran out. This is what says
    ///     the filter that prevents it is still there.
    /// </remarks>
    [Fact]
    public void TheCompositeCollectsTheSourcesAndNotItself()
    {
        using var application = AnApplicationWithTwoSources();

        application.GetRequiredService<CompositeLocalizationProvider>().ProviderCount
            .Should().Be(2, "the two JSON directories, and nothing that resolves back to the composite");
    }
}
