using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Internationalization.AspNetCore.Extensions;
using Pragmatic.Internationalization.AspNetCore.Providers;
using Pragmatic.Internationalization.Context;
using Pragmatic.Internationalization.Types;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Internationalization.Tests.Providers;

/// <summary>
///     Where the application configures no culture, the languages its modules' translations are written in
///     are the configuration; where nothing gives a default, the host does not start.
/// </summary>
/// <remarks>
///     The provider is what the generated host registers from the translation metadata; its arguments here
///     are what a module with <c>translations/en.json</c> and <c>it.json</c> declares.
/// </remarks>
public class TheDeclaredLanguagesAreTheDefaultTests
{
    [Fact]
    public void WithNothingConfigured_TheDeclaredLanguagesAreTheConfiguration()
    {
        var services = Services(declared: new DeclaredLanguagesConfigProvider("en", ["en", "it"]));

        Start(services).Should().NotThrow();
        var config = Resolve(services);
        config.DefaultUICulture.Should().Be(CultureCode.FromString("en"));
        config.SupportedCultures.Should().BeEquivalentTo([CultureCode.FromString("en"), CultureCode.FromString("it")]);
    }

    /// <summary>What the application configures wins; what it leaves out still comes from the modules.</summary>
    [Fact]
    public void TheApplicationsDefault_WinsOverTheDeclaredOne()
    {
        var services = Services(
            declared: new DeclaredLanguagesConfigProvider("en", ["en", "it"]),
            configure: options => options.DefaultUICulture = CultureCode.FromString("it-IT"));

        var config = Resolve(services);
        config.DefaultUICulture.Should().Be(CultureCode.FromString("it-IT"));
        config.SupportedCultures.Should().BeEquivalentTo([CultureCode.FromString("en"), CultureCode.FromString("it")]);
    }

    /// <summary>The control: with neither a configured nor a declared default the host refuses to start.</summary>
    [Fact]
    public void WithoutADefaultFromAnywhere_TheHostDoesNotStart()
    {
        var services = Services(declared: new DeclaredLanguagesConfigProvider(null, ["it", "de"]));

        Start(services).Should().Throw<I18NConfigurationException>(
            "the modules are translated into it and de, and none of them is the culture they are written from");
    }

    /// <summary>
    ///     A provider that answers per request cannot be asked outside one: the default may come from it,
    ///     so the check stays with the request.
    /// </summary>
    [Fact]
    public void AProviderThatAnswersPerRequest_LeavesTheCheckToTheRequest()
    {
        var services = Services(declared: null);
        services.AddScoped<II18NConfigProvider, PerRequestProvider>();

        Start(services).Should().NotThrow();
    }

    private static ServiceCollection Services(
        DeclaredLanguagesConfigProvider? declared, Action<I18NOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddPragmaticInternationalization(configure);
        if (declared is not null)
            services.AddSingleton<II18NConfigProvider>(declared);
        return services;
    }

    private static Action Start(ServiceCollection services)
        => () => new ApplicationBuilder(services.BuildServiceProvider()).UsePragmaticInternationalization();

    private static I18NConfig Resolve(ServiceCollection services)
    {
        using var scope = services.BuildServiceProvider().CreateScope();
        return scope.ServiceProvider.GetRequiredService<I18NConfigResolver>().Resolve();
    }

    private sealed class PerRequestProvider : II18NConfigProvider
    {
        public int Priority => 100;

        public I18NConfig? GetConfiguration() => null;
    }
}
