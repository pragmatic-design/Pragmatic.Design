using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Pragmatic.Internationalization.AspNetCore.Middleware;
using Pragmatic.Internationalization.AspNetCore.Providers;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Types;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Internationalization.Tests.Providers;

/// <summary>
///     A <see cref="LocalizedString" /> read in a culture it does not hold answers the culture the
///     application's fallback chain names, through the context the request carries, and not "en".
/// </summary>
[Collection("I18nContext")]
public class ALocalizedStringFollowsTheFallbackChainTests
{
    private static readonly CultureCode Italian = CultureCode.FromString("it");
    private static readonly CultureCode German = CultureCode.FromString("de");

    private static readonly LocalizedString Name = LocalizedString.From(("it", "Componente"), ("en", "Widget"));

    [Fact]
    public async Task AConfiguredChain_ComesBeforeEnglish()
    {
        var read = await ReadInGerman(new I18NOptions
        {
            DefaultUICulture = CultureCode.EnglishUS,
            SupportedCultures = [CultureCode.EnglishUS, Italian, German],
            FallbackChain = _ => [Italian]
        });

        read.Should().Be("Componente");
    }

    [Fact]
    public async Task TheApplicationsDefaultCulture_IsTheDefaultChain()
    {
        var read = await ReadInGerman(new I18NOptions
        {
            DefaultUICulture = CultureCode.FromString("it-IT"),
            SupportedCultures = [CultureCode.FromString("it-IT"), CultureCode.EnglishUS, German]
        });

        read.Should().Be("Componente");
    }

    [Fact]
    public async Task ADefaultAProviderResolves_IsTheDefaultChain()
    {
        // No UseI18N: the default is the language the modules' translations are written from.
        var options = new I18NOptions();
        var opts = Options.Create(options);
        var resolver = new I18NConfigResolver(
        [
            new SystemConfigProvider(opts),
            new DeclaredLanguagesConfigProvider("it", ["it", "en", "de"])
        ]);

        string? read = null;
        await Invoke(resolver, opts, () => read = Name.Value);

        read.Should().Be("Componente");
    }

    [Fact]
    public async Task AnEnglishDefault_StillAnswersEnglish()
    {
        var read = await ReadInGerman(new I18NOptions
        {
            DefaultUICulture = CultureCode.EnglishUS,
            SupportedCultures = [CultureCode.EnglishUS, Italian, German]
        });

        read.Should().Be("Widget");
    }

    [Fact]
    public void WithNoChainInTheContext_EnglishIsTheLastStep()
    {
        var snapshot = I18NContext.Capture();
        try
        {
            I18NContext.SetCulture(German);

            Name.Value.Should().Be("Widget");
        }
        finally
        {
            I18NContext.Restore(snapshot);
        }
    }

    [Fact]
    public async Task ACultureTheValueHolds_IsReadAsItIs()
    {
        var name = LocalizedString.From(("it", "Componente"), ("en", "Widget"), ("de", "Bauteil"));
        string? read = null;

        await Invoke(new I18NOptions
        {
            DefaultUICulture = CultureCode.EnglishUS,
            SupportedCultures = [CultureCode.EnglishUS, Italian, German],
            FallbackChain = _ => [Italian]
        }, () => read = name.Value);

        read.Should().Be("Bauteil");
    }

    private static async Task<string?> ReadInGerman(I18NOptions options)
    {
        string? read = null;
        await Invoke(options, () => read = Name.Value).ConfigureAwait(false);
        return read;
    }

    private static Task Invoke(I18NOptions options, Action duringTheRequest)
    {
        var opts = Options.Create(options);
        return Invoke(new I18NConfigResolver([new SystemConfigProvider(opts)]), opts, duringTheRequest);
    }

    private static async Task Invoke(I18NConfigResolver resolver, IOptions<I18NOptions> opts, Action duringTheRequest)
    {
        var middleware = new I18NContextMiddleware(_ =>
        {
            duringTheRequest();
            return Task.CompletedTask;
        });

        var context = new DefaultHttpContext();
        context.Request.Headers.AcceptLanguage = "de";

        var snapshot = I18NContext.Capture();
        try
        {
            await middleware.InvokeAsync(context, resolver, opts).ConfigureAwait(false);
        }
        finally
        {
            I18NContext.Restore(snapshot);
        }
    }
}
