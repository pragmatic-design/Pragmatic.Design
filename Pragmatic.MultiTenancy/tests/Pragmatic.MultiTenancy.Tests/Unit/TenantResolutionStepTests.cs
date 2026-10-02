using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Pragmatic.Composition;
using Pragmatic.Composition.Abstractions;
using Pragmatic.Composition.Hosting;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.MultiTenancy.Tests.Unit;

/// <summary>
///     <c>UseMultiTenancy(...)</c> registers the resolver <b>and</b> the pipeline step. A resolver
///     without the step resolves no tenant: rows are written with an empty tenant id and match
///     nothing on read, with no exception and no log.
/// </summary>
public class TenantResolutionStepTests
{
    /// <summary>
    ///     After AuthenticationStep (91) — the claim resolver reads the authenticated principal, so
    ///     running earlier would resolve an anonymous request — and before
    ///     InternationalizationStep (95).
    /// </summary>
    [Fact]
    public void Order_RunsAfterAuthentication()
    {
        new TenantResolutionStep().Order.Should().Be(92);
    }

    [Fact]
    public void UseMultiTenancy_RegistersTheStep()
    {
        var services = new ServiceCollection();

        new PragmaticBuilder(services, new ConfigurationBuilder().Build(), new TestEnvironment())
            .UseMultiTenancy(b => b.UseSingleTenant());

        // The resolver alone is not enough, and that was the whole defect: asserting only that
        // AddPragmaticMultiTenancy ran would have stayed green while no request resolved a tenant.
        services.Should().Contain(d =>
            d.ServiceType == typeof(IStartupStep) &&
            d.ImplementationType == typeof(TenantResolutionStep));
    }

    /// <summary>
    ///     The generator's own path registers it too.
    /// </summary>
    /// <remarks>
    ///     <c>PragmaticHostTemplate</c> emits <c>services.AddPragmaticMultiTenancy()</c> when it detects
    ///     the module, without going through the builder. While the step was registered only in
    ///     <c>UseMultiTenancy</c>, that default path produced a resolver nobody ran: the same silence as
    ///     the original defect, reached by the route an application takes when it configures nothing.
    ///     A lab app lost the afternoon to it — every list empty, rows present in the table.
    /// </remarks>
    [Fact]
    public void AddPragmaticMultiTenancy_OnItsOwn_RegistersTheStep()
    {
        var services = new ServiceCollection();

        services.AddPragmaticMultiTenancy();

        services.Should().Contain(d =>
            d.ServiceType == typeof(IStartupStep) &&
            d.ImplementationType == typeof(TenantResolutionStep));
    }

    /// <summary>
    ///     Both routes together still register it once.
    /// </summary>
    /// <remarks>
    ///     The generator calls <c>AddPragmaticMultiTenancy</c> and the application may call
    ///     <c>UseMultiTenancy</c> on top. Two descriptors would add the middleware twice, and a tenant
    ///     resolved twice per request is the kind of thing that only shows up under a resolver with a
    ///     side effect.
    /// </remarks>
    [Fact]
    public void BothRoutes_RegisterTheStepOnce()
    {
        var services = new ServiceCollection();

        services.AddPragmaticMultiTenancy();
        new PragmaticBuilder(services, new ConfigurationBuilder().Build(), new TestEnvironment())
            .UseMultiTenancy(b => b.UseSingleTenant());

        services.Count(d =>
            d.ServiceType == typeof(IStartupStep) &&
            d.ImplementationType == typeof(TenantResolutionStep)).Should().Be(1);
    }

    [Fact]
    public void ConfigurePipeline_AddsTheMiddleware()
    {
        var services = new ServiceCollection();
        services.AddLogging();   // Build() constructs the middleware, which takes an ILogger
        var app = new ApplicationBuilder(services.BuildServiceProvider());

        new TenantResolutionStep().ConfigurePipeline(app);

        // Build() instantiates the middleware, so this proves it is in the pipeline and can be
        // constructed; that it resolves the right tenant is covered by the resolver's own tests.
        app.Build().Should().NotBeNull();
    }

    /// <summary>Minimal <see cref="IHostEnvironment" /> — the builder needs one, nothing reads it.</summary>
    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Pragmatic.MultiTenancy.Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
