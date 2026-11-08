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
///     A provider that reads its configuration asynchronously (a database the engines read only asynchronously) sets the request's culture
///     through the middleware (#591). The chain was synchronous at the contract: the middleware called <c>Resolve()</c>, which called each
///     provider's <c>GetConfiguration()</c>, so such a provider had nothing to answer with.
/// </summary>
[Collection("I18nContext")]
public class AnAsynchronousProviderSetsTheRequestCultureTests
{
    private static readonly CultureCode Italian = CultureCode.FromString("it-IT");

    /// <summary>Loads only asynchronously: its synchronous answer is the cache, and on a miss nothing.</summary>
    private sealed class AsynchronousProvider(string key) : CachedConfigProvider(TimeSpan.FromMinutes(5))
    {
        public int Loads { get; private set; }

        public override int Priority => 200;

        protected override bool LoadsAsynchronously => true;

        protected override string? GetCacheKey() => key;

        protected override I18NConfig? LoadConfiguration() => throw new InvalidOperationException("an asynchronous provider is never loaded synchronously");

        protected override async ValueTask<I18NConfig?> LoadConfigurationAsync(CancellationToken ct)
        {
            await Task.Yield();
            Loads++;
            return new I18NConfig { DefaultUICulture = Italian };
        }
    }

    /// <summary>Loads synchronously, as every provider did before.</summary>
    private sealed class SynchronousProvider(string key) : CachedConfigProvider(TimeSpan.FromMinutes(5))
    {
        public int Loads { get; private set; }

        public override int Priority => 200;

        protected override string? GetCacheKey() => key;

        protected override I18NConfig? LoadConfiguration()
        {
            Loads++;
            return new I18NConfig { DefaultUICulture = Italian };
        }
    }

    [Fact]
    public async Task TheMiddleware_SetsTheCultureAnAsynchronousProviderLoads()
    {
        var culture = await CultureThroughTheMiddleware(new AsynchronousProvider("async-" + Guid.NewGuid()));

        culture.Should().Be("it-IT");
    }

    /// <summary>The control: a provider with only the synchronous load still sets it.</summary>
    [Fact]
    public async Task TheMiddleware_SetsTheCultureASynchronousProviderLoads()
    {
        var culture = await CultureThroughTheMiddleware(new SynchronousProvider("sync-" + Guid.NewGuid()));

        culture.Should().Be("it-IT");
    }

    /// <summary>One load per key within the duration, whichever path asks first; the synchronous path answers from what the asynchronous one cached.</summary>
    [Fact]
    public async Task TheTwoPaths_ShareOneCache()
    {
        var key = "shared-" + Guid.NewGuid();
        var asynchronous = new AsynchronousProvider(key);

        asynchronous.GetConfiguration().Should().BeNull("nothing is cached yet, and an asynchronous provider does not load synchronously");
        (await asynchronous.GetConfigurationAsync())!.DefaultUICulture.Should().Be(Italian);
        (await asynchronous.GetConfigurationAsync())!.DefaultUICulture.Should().Be(Italian);
        asynchronous.GetConfiguration()!.DefaultUICulture.Should().Be(Italian);
        asynchronous.Loads.Should().Be(1);

        var synchronous = new SynchronousProvider("shared-sync-" + Guid.NewGuid());
        synchronous.GetConfiguration()!.DefaultUICulture.Should().Be(Italian);
        (await synchronous.GetConfigurationAsync())!.DefaultUICulture.Should().Be(Italian);
        synchronous.Loads.Should().Be(1);
    }

    private static async Task<string?> CultureThroughTheMiddleware(II18NConfigProvider provider)
    {
        var options = Options.Create(new I18NOptions { DefaultUICulture = CultureCode.EnglishUS, SupportedCultures = [CultureCode.EnglishUS, Italian] });
        var resolver = new I18NConfigResolver([new SystemConfigProvider(options), provider]);
        string? observed = null;
        var middleware = new I18NContextMiddleware(_ =>
        {
            observed = I18NContext.Current.CultureCode;
            return Task.CompletedTask;
        });

        var snapshot = I18NContext.Capture();
        try
        {
            await middleware.InvokeAsync(new DefaultHttpContext(), resolver, options).ConfigureAwait(false);
        }
        finally
        {
            I18NContext.Restore(snapshot);
        }

        return observed;
    }
}
