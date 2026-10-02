using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Composition;
using Pragmatic.Composition.Abstractions;
using Pragmatic.Endpoints.Mcp;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Endpoints.Mcp.Tests;

/// <summary>
///     The door this package is documented as having.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <c>[McpTool]</c>'s own XML comment says the endpoint is exposed «when the host enables
///         <c>UseMcp()</c>». The mechanism is <c>services.AddPragmaticMcp()</c>, which the Showcase
///         calls; this pins that the name in the documentation reaches it.
///     </para>
///     <para>
///         It matters beyond the name: every other module that has a host-level choice offers
///         <c>Use{Module}()</c> on the builder (configuration in three tiers), and a reader who follows the convention, or the
///         attribute, writes a call that does not compile.
///     </para>
/// </remarks>
public class UseMcpTests
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

    /// <summary>The call the attribute names registers what serves a tool.</summary>
    /// <remarks>
    ///     Asserted on the three services a tool call actually goes through — the catalog that lists
    ///     them, the executor that runs them, and the startup step that maps the route — rather than on
    ///     "some service was added", which any call would satisfy.
    /// </remarks>
    [Fact]
    public void UseMcp_RegistersWhatServesATool()
    {
        var builder = new FakeBuilder();

        builder.UseMcp();

        var provider = builder.Services.BuildServiceProvider();
        provider.GetService<ManifestToolCatalog>().Should().NotBeNull("a tool list comes from the manifest catalog");
        provider.GetService<McpToolExecutor>().Should().NotBeNull("a tool call goes through the executor");
        provider.GetServices<IStartupStep>().Should().Contain(s => s is McpStartupStep,
            "and the route is mapped by the startup step");
    }

    /// <summary>The configuration reaches the options the server reads.</summary>
    [Fact]
    public void UseMcp_CarriesTheOptions()
    {
        var builder = new FakeBuilder();

        builder.UseMcp(o =>
        {
            o.Path = "/tools";
            o.RequireAuthorization = false;
        });

        var options = builder.Services.BuildServiceProvider().GetRequiredService<McpOptions>();
        options.Path.Should().Be("/tools");
        options.RequireAuthorization.Should().BeFalse();
    }

    /// <summary>The control: without the call, nothing of this package is registered.</summary>
    /// <remarks>
    ///     Without it, "the catalog is there" would be satisfied by a package that registers itself on
    ///     reference — which is the opposite of opt-in, and would put an anonymous-by-default surface
    ///     into every application that merely takes the dependency.
    /// </remarks>
    [Fact]
    public void WithoutTheCall_NothingIsRegistered()
    {
        var builder = new FakeBuilder();

        var provider = builder.Services.BuildServiceProvider();
        provider.GetService<ManifestToolCatalog>().Should().BeNull();
        provider.GetServices<IStartupStep>().Should().NotContain(s => s is McpStartupStep);
    }

    /// <summary>And it is the same registration <c>AddPragmaticMcp</c> makes, not a second one.</summary>
    /// <remarks>
    ///     The builder form is a name, not a mechanism: two doors onto one room. If they diverged, an
    ///     application would behave differently depending on which one it opened.
    /// </remarks>
    [Fact]
    public void UseMcp_IsTheServiceCollectionCallUnderAnotherName()
    {
        var viaBuilder = new FakeBuilder();
        viaBuilder.UseMcp(o => o.Path = "/tools");

        var viaServices = new ServiceCollection();
        viaServices.AddPragmaticMcp(o => o.Path = "/tools");

        viaBuilder.Services.Select(d => d.ServiceType.FullName).OrderBy(n => n)
            .Should().Contain(viaServices.Select(d => d.ServiceType.FullName).OrderBy(n => n));
    }
}
