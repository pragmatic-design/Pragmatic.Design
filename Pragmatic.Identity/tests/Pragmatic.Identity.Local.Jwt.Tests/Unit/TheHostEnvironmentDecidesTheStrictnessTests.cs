using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Pragmatic.Composition;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Identity.Local.Jwt.Tests.Unit;

/// <summary>
///     Whether <c>UseJwtAuthentication</c> is strict is decided by the environment of the host it
///     configures.
/// </summary>
/// <remarks>
///     <para>
///         It does not read <c>ASPNETCORE_ENVIRONMENT</c> and <c>DOTNET_ENVIRONMENT</c> from the
///         process. Reading them, a host set to Development any other way —
///         <c>WebApplicationFactory.UseEnvironment</c>, <c>--environment</c>,
///         <c>builder.Environment.EnvironmentName</c> — would get the strict branch anyway, while its
///         <c>HeaderUserMiddleware</c>, which reads <c>IHostEnvironment</c>, sees Development: two
///         components of one host disagreeing about which environment it is.
///     </para>
///     <para>
///         Both tests set the builder's environment and leave the process variables alone. The Staging one
///         is the control: every environment but Development stays strict.
///     </para>
/// </remarks>
public class TheHostEnvironmentDecidesTheStrictnessTests
{
    private const string Key = "host-environment-signing-key-at-least-32!";

    [Fact]
    public void ADevelopmentHost_AcceptsOptionsWithoutIssuerOrAudience_AndPlainHttpMetadata()
    {
        var provider = Configure(Environments.Development);

        Action resolve = () => _ = provider.GetRequiredService<IOptions<JwtOptions>>().Value;
        resolve.Should().NotThrow("the host says Development, so issuer and audience are not required");
        Bearer(provider).RequireHttpsMetadata.Should().BeFalse();
    }

    [Fact]
    public void AStagingHost_StillRefusesThem()
    {
        var provider = Configure(Environments.Staging);

        Action resolve = () => _ = provider.GetRequiredService<IOptions<JwtOptions>>().Value;
        resolve.Should().Throw<OptionsValidationException>();
        Bearer(provider).RequireHttpsMetadata.Should().BeTrue();
    }

    private static ServiceProvider Configure(string environment)
    {
        var builder = new FakeBuilder(environment);
        builder.UseJwtAuthentication(jwt => jwt.SigningKey = Key);
        return builder.Services.BuildServiceProvider();
    }

    private static JwtBearerOptions Bearer(ServiceProvider provider)
        => provider.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);

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
