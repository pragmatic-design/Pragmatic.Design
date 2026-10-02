using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Composition;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Identity.Oidc.Tests;

/// <summary>
///     Whether <c>UseOidcAuthentication</c> requires an audience is decided by the environment of the host
///     it configures.
/// </summary>
/// <remarks>
///     It reads the host's environment, not <c>ASPNETCORE_ENVIRONMENT</c>/<c>DOTNET_ENVIRONMENT</c> from
///     the process, which would refuse an empty audience to a host set to Development by any other means.
///     The Staging test is the control: outside Development an empty audience is still refused.
/// </remarks>
public class TheHostEnvironmentDecidesTheStrictnessTests
{
    [Fact]
    public void ADevelopmentHost_AcceptsNoAudience()
    {
        var builder = new FakeBuilder(Environments.Development);

        var act = () => builder.UseOidcAuthentication(o => o.Authority = "https://idp.example.com/realms/myrealm");

        act.Should().NotThrow("the host says Development, so an empty audience is allowed");
    }

    [Fact]
    public void AStagingHost_StillRefusesNoAudience()
    {
        var builder = new FakeBuilder(Environments.Staging);

        var act = () => builder.UseOidcAuthentication(o => o.Authority = "https://idp.example.com/realms/myrealm");

        act.Should().Throw<InvalidOperationException>();
    }

    private sealed class FakeBuilder(string environment) : IPragmaticBuilder
    {
        public IServiceCollection Services { get; } = new ServiceCollection();
        public IConfiguration Configuration { get; } = new ConfigurationBuilder().Build();
        public IHostEnvironment Environment { get; } = new FakeEnvironment { EnvironmentName = environment };
    }

    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "Tests";
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ContentRootPath { get; set; } = Path.GetTempPath();
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
