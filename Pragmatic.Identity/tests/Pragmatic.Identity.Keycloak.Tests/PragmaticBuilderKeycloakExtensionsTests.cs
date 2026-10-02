using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Composition;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Identity.Keycloak.Tests;

/// <summary>
///     The one way to turn this package on.
/// </summary>
/// <remarks>
///     <c>UseKeycloakAuthentication</c> is the only entry point the package has, and nothing in the
///     repository called it, no test ran it and no page named it. A package whose single door is
///     unproven is one where a break is found by the first person to deploy it.
/// </remarks>
public class PragmaticBuilderKeycloakExtensionsTests
{
    private sealed class FakeBuilder : IPragmaticBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();
        public IConfiguration Configuration { get; } = new ConfigurationBuilder().Build();
        public IHostEnvironment Environment { get; } = new FakeEnvironment();
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    [Fact]
    public void UseKeycloakAuthentication_RegistersJwtBearerAuthentication()
    {
        var builder = new FakeBuilder();

        builder.UseKeycloakAuthentication(k =>
        {
            k.BaseUrl = "https://keycloak.example.com";
            k.Realm = "myrealm";
            k.Audience = "my-api";
        });

        var provider = builder.Services.BuildServiceProvider();
        provider.GetService<IAuthenticationSchemeProvider>()
            .Should().NotBeNull("the point of the call is that the application can authenticate");
    }

    /// <remarks>
    ///     Without a realm there is no authority to validate against, so failing at configuration time
    ///     is the difference between a startup error and tokens validated against nothing.
    /// </remarks>
    [Theory]
    [InlineData("", "myrealm")]
    [InlineData("https://keycloak.example.com", "")]
    public void UseKeycloakAuthentication_WithoutBaseUrlOrRealm_Throws(string baseUrl, string realm)
    {
        var builder = new FakeBuilder();

        var act = () => builder.UseKeycloakAuthentication(k =>
        {
            k.BaseUrl = baseUrl;
            k.Realm = realm;
        });

        act.Should().Throw<ArgumentException>();
    }
}
