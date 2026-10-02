using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Pragmatic.Composition;
using Pragmatic.Identity.Local.Services;
using Pragmatic.Testing.Assertions;
using Xunit;
using PackageServices = Pragmatic.Identity.Local.Services.ServiceRegistrationExtensions;

namespace Pragmatic.Identity.Local.Jwt.Tests.Unit;

/// <summary>
///     A host imports the local identity package and signs no token (the Showcase in development,
///     a host whose tokens come from elsewhere). Every package action's invoker is registered, and the
///     sign-in's takes an <see cref="IAccessTokenIssuer" />: without one, the container's validation refuses
///     the whole host at startup, although nothing it serves signs anyone in.
/// </summary>
public class AHostWithoutAnIssuerTests
{
    private const string Key = "a-host-without-an-issuer-key-at-least-32!";

    [Fact]
    public void ThePackageAlone_ProvidesAnIssuer_ThatRefusesAndNamesTheFix()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        PackageServices.AddPragmaticServices(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });

        var issuer = provider.GetRequiredService<IAccessTokenIssuer>();
        var act = () => issuer.Issue(new SignInClaims(new LocalIdentity { ExternalIdentityKey = "local|ada@example.com" }));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*UseJwtAuthentication*");
    }

    /// <summary>The control: the package's default never shadows the issuer the host chose, whichever registers first.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UseJwtAuthentication_ReplacesThePackagesDefault(bool packageFirst)
    {
        var builder = new FakeBuilder();
        if (packageFirst)
            PackageServices.AddPragmaticServices(builder.Services);
        builder.UseJwtAuthentication(jwt => jwt.SigningKey = Key);
        if (!packageFirst)
            PackageServices.AddPragmaticServices(builder.Services);
        using var provider = builder.Services.BuildServiceProvider();

        provider.GetRequiredService<IAccessTokenIssuer>().Should().BeSameAs(
            provider.GetRequiredService<JwtTokenGenerator>(), "the host's issuer, not the package's refusal");
    }

    private sealed class FakeBuilder : IPragmaticBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();
        public IConfiguration Configuration { get; } = new ConfigurationBuilder().Build();
        public IHostEnvironment Environment { get; } = new FakeEnvironment();
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
