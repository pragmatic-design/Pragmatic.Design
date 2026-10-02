using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Composition;
using Pragmatic.Temporal.Clock;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Temporal.Tests;

/// <summary>
///     The one way to turn this module on from the Pragmatic builder.
/// </summary>
/// <remarks>
///     <c>UseTemporal</c> is what a host calls, and nothing called it, no test ran it and no page
///     named it. The configuration callback is the half worth asserting: a builder that registers the
///     module but never runs what it was handed would leave every custom provider unregistered while
///     looking like it worked.
/// </remarks>
public class PragmaticBuilderTemporalExtensionsTests
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
    public void UseTemporal_RegistersTheClock()
    {
        var builder = new FakeBuilder();

        builder.UseTemporal(_ => { });

        builder.Services.BuildServiceProvider().GetService<IClock>()
            .Should().NotBeNull("time is what the module is for");
    }

    [Fact]
    public void UseTemporal_RunsTheConfiguration()
    {
        var builder = new FakeBuilder();
        var ran = false;

        builder.UseTemporal(_ => ran = true);

        ran.Should().BeTrue("a callback that is never invoked leaves every choice in it undone");
    }
}
