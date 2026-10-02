using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Composition;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Identity.Oidc.Tests;

/// <summary>
///     The one way to turn this package on.
/// </summary>
/// <remarks>
///     <c>UseOidcAuthentication</c> is the package's only entry point, and nothing called it, no test
///     ran it and no page named it.
/// </remarks>
public class PragmaticBuilderOidcExtensionsTests
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
    public void UseOidcAuthentication_RegistersJwtBearerAuthentication()
    {
        var builder = new FakeBuilder();

        builder.UseOidcAuthentication(o =>
        {
            o.Authority = "https://idp.example.com";
            o.Audience = "my-api";
        });

        builder.Services.BuildServiceProvider().GetService<IAuthenticationSchemeProvider>()
            .Should().NotBeNull("the point of the call is that the application can authenticate");
    }

    /// <remarks>
    ///     No authority means no issuer to validate against. Failing here is the difference between a
    ///     startup error and tokens checked against nothing.
    /// </remarks>
    [Fact]
    public void UseOidcAuthentication_WithoutAuthority_Throws()
    {
        var builder = new FakeBuilder();

        var act = () => builder.UseOidcAuthentication(o => o.Audience = "my-api");

        act.Should().Throw<ArgumentException>();
    }
}
