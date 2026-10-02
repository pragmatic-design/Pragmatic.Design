using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pragmatic.Logging.AspNetCore;
using Pragmatic.Logging.Context;
using Pragmatic.Logging.Extensions;
using Pragmatic.Logging.Providers;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Logging.Tests;

/// <summary>
///     The ways in are exercised at least once each.
/// </summary>
/// <remarks>
///     <para>
///         These are public entry points that no caller in this repository used, no test ran and no page
///         described. That combination is not proof of dead code — a framework has its callers outside —
///         but it does mean nothing would have noticed if they had stopped working, and nobody could
///         have found them.
///     </para>
///     <para>
///         Kept deliberately shallow: each asserts that the entry point does the one observable thing it
///         exists for. The point is that the door opens, not what is behind it.
///     </para>
/// </remarks>
public class EntryPointsTests
{
    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }

    /// <summary>
    ///     A method called Add Pragmatic logging leaves Pragmatic logging in place.
    /// </summary>
    /// <remarks>
    ///     This is the assertion that found the defect. Every entry point in the family landed on a
    ///     private AddPragmaticLoggingCore that registered redaction, secret detection and audit and
    ///     never touched ILoggerFactory — so the preset configured how logging should behave and left
    ///     nothing to behave that way. Two of those entry points were also called AddPragmaticLogging,
    ///     like the overload that does wire it, separated only by the lambda's parameter type — they
    ///     are now AddPragmaticLoggingFromConfiguration and AddPragmaticLoggingWithOptions.
    /// </remarks>
    [Fact]
    public void AddPragmaticLoggingForMicroservice_LeavesPragmaticLoggingInPlace()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPragmaticLoggingForMicroservice("orders");

        services.BuildServiceProvider().GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()
            .Should().BeOfType<PragmaticLoggerFactory>();
    }

    [Fact]
    public void AddPragmaticLoggingForMicroservice_PutsTheServiceNameInTheContext()
    {
        var services = new ServiceCollection();

        services.AddPragmaticLoggingForMicroservice("orders");

        var options = services.BuildServiceProvider()
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Pragmatic.Logging.Configuration.PragmaticLoggingOptions>>().Value;

        options.Context.CustomProperties.Should().ContainKey("ServiceName");
        options.Context.CustomProperties["ServiceName"].Should().Be("orders");
        options.Context.IncludeCorrelationId.Should().BeTrue("a microservice is traced across hosts");
    }

    [Fact]
    public void AddPragmaticLoggingWithSmartPreset_ReadsTheEnvironment()
    {
        var services = new ServiceCollection();

        services.AddPragmaticLoggingWithSmartPreset(
            new TestEnvironment { EnvironmentName = Environments.Development });

        services.BuildServiceProvider()
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<Pragmatic.Logging.Configuration.PragmaticLoggingOptions>>()
            .Value.MinimumLevel.Should().Be(Microsoft.Extensions.Logging.LogLevel.Debug,
                "the smart preset picks its level from the environment it is handed");
    }

    /// <remarks>
    ///     Named, because the name is how it is fetched back out again.
    /// </remarks>
    [Fact]
    public void AddJsonProvider_PutsANamedProviderInTheRegistry()
    {
        using var registry = new PragmaticLoggerProviderRegistry();

        registry.AddJsonProvider();

        registry.GetProvider("json").Should().NotBeNull();
        registry.Providers.Should().ContainKey("json");
    }

    [Fact]
    public void AddJsonProvider_WithAName_UsesIt()
    {
        using var registry = new PragmaticLoggerProviderRegistry();

        registry.AddJsonProvider("audit");

        registry.GetProvider("audit").Should().NotBeNull();
        registry.GetProvider("json").Should().BeNull();
    }

    /// <remarks>
    ///     A middleware that throws when it is added is the failure worth catching here: the pipeline is
    ///     built at startup, so it would take the whole application down rather than one request.
    /// </remarks>
    [Fact]
    public void UsePragmaticLogging_BuildsAPipelineThatRuns()
    {
        // AddLogging because the middleware takes an ILogger<T>: every real host has it, and a bare
        // collection here would be testing the absence of the framework rather than this middleware.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticLoggingForMicroservice("orders");
        var app = new ApplicationBuilder(services.BuildServiceProvider());

        app.UsePragmaticLogging();
        app.Run(_ => Task.CompletedTask);

        app.Build().Should().NotBeNull();
    }

    [Fact]
    public void UsePragmaticBaggage_BuildsAPipelineThatRuns()
    {
        // AddLogging because the middleware takes an ILogger<T>: every real host has it, and a bare
        // collection here would be testing the absence of the framework rather than this middleware.
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPragmaticLoggingForMicroservice("orders");
        var app = new ApplicationBuilder(services.BuildServiceProvider());

        app.UsePragmaticBaggage();
        app.Run(_ => Task.CompletedTask);

        app.Build().Should().NotBeNull();
    }
}

/// <summary>
///     Three ways in, three names.
/// </summary>
/// <remarks>
///     <para>
///         They were all called <c>AddPragmaticLogging</c>, on <c>IServiceCollection</c>, across three
///         namespaces: the builder one that wires logging, the options one that binds configuration,
///         and the ASP.NET one that adds HTTP context enrichment. The parameterless forms were an
///         outright ambiguity when both namespaces were imported; the lambda forms were worse, because
///         the compiler picked one by inferring the parameter type and said nothing.
///     </para>
///     <para>
///         The module was already working around it: <c>PresetExtensions</c> delegated with
///         <c>(PragmaticLoggingOptions _) =&gt; { }</c>, spelling out the type to force the overload.
///         That line is what a workaround looks like when it has been there long enough to read as
///         style.
///     </para>
///     <para>
///         This file imports all three namespaces on purpose. If two of these names collided again it
///         would not compile, which is the assertion.
///     </para>
/// </remarks>
public class EntryPointNamesTests
{
    [Fact]
    public void TheBuilderEntryPoint_WiresLogging()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPragmaticLogging(_ => { });

        services.BuildServiceProvider().GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()
            .Should().BeOfType<PragmaticLoggerFactory>();
    }

    [Fact]
    public void TheOptionsEntryPoint_WiresLoggingAndAppliesTheOptions()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddPragmaticLoggingWithOptions(o =>
            o.MinimumLevel = Microsoft.Extensions.Logging.LogLevel.Warning);

        var provider = services.BuildServiceProvider();
        provider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()
            .Should().BeOfType<PragmaticLoggerFactory>();
        provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<Pragmatic.Logging.Configuration.PragmaticLoggingOptions>>()
            .Value.MinimumLevel.Should().Be(Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    /// <remarks>
    ///     The HTTP one adds context providers and deliberately does not wire logging: it decorates
    ///     what the other two arrange.
    /// </remarks>
    [Fact]
    public void TheHttpEntryPoint_AddsContextProviders()
    {
        var services = new ServiceCollection();

        services.AddPragmaticHttpLogging(o => o.EnableCorrelationIdTracking = true);

        services.Should().Contain(d => d.ServiceType == typeof(IContextProvider));
    }
}
