using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.MultiTenancy.Resolvers;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.MultiTenancy.Tests.Unit;

/// <summary>
///     Several <c>Use*</c> strategies are tried in call order, and the first that answers wins.
/// </summary>
/// <remarks>
///     <para>
///         If every <c>Use*</c> registered itself as <em>the</em> <see cref="ITenantResolver" />, the
///         last call would replace the others: <c>UseHeader().UseClaim()</c> would read the claim only,
///         and a request carrying the header would resolve no tenant. The documented chain,
///         <c>.UseHeader().UseClaim().UseSingleTenant("demo")</c>, would make every request tenant
///         <c>demo</c>. Several strategies are wrapped in a <see cref="CompositeTenantResolver" />.
///     </para>
///     <para>
///         Measured on the resolver the container hands out, over a real request: what the
///         middleware gets is what decides the tenant.
///     </para>
/// </remarks>
public sealed class SeveralStrategiesChainTests
{
    [Fact]
    public async Task HeaderThenClaim_ARequestWithOnlyTheHeader_ResolvesTheHeadersTenant()
    {
        var tenant = await Resolve(mt => mt.UseHeader().UseClaim(), header: "acme", claim: null);

        tenant.Should().Be("acme", "the claim answers nothing, so the chain goes on to the header before it");
    }

    /// <summary>The control: the other strategy of the same chain still answers when it is the one set.</summary>
    [Fact]
    public async Task HeaderThenClaim_ARequestWithOnlyTheClaim_ResolvesTheClaimsTenant()
        => (await Resolve(mt => mt.UseHeader().UseClaim(), header: null, claim: "globex")).Should().Be("globex");

    /// <summary>Call order is the order tried: with both present, the first strategy wins.</summary>
    [Fact]
    public async Task HeaderThenClaim_ARequestWithBoth_ResolvesTheFirstStrategy()
        => (await Resolve(mt => mt.UseHeader().UseClaim(), header: "acme", claim: "globex")).Should().Be("acme");

    /// <summary>
    ///     The documented example: a fixed tenant at the end is the fallback, not a replacement.
    /// </summary>
    [Fact]
    public async Task AFixedTenantLast_IsTheFallback_NotTheAnswerToEveryRequest()
    {
        (await Resolve(mt => mt.UseHeader().UseClaim().UseSingleTenant("demo"), header: "acme", claim: null))
            .Should().Be("acme");
        (await Resolve(mt => mt.UseHeader().UseClaim().UseSingleTenant("demo"), header: null, claim: null))
            .Should().Be("demo");
    }

    /// <summary>One strategy is registered as itself: no composite, nothing to pay per request.</summary>
    [Fact]
    public void OneStrategy_IsRegisteredDirectly()
    {
        var services = new ServiceCollection().AddPragmaticMultiTenancy(mt => mt.UseHeader());

        services.Where(d => d.ServiceType == typeof(ITenantResolver)).Should().ContainSingle()
            .Which.ImplementationType.Should().Be(typeof(HeaderTenantResolver));
    }

    /// <summary>
    ///     The generator registers the single-tenant default first; the application's strategy then
    ///     replaces it rather than joining it — or a request with no header would fall through to
    ///     <c>"default"</c> instead of resolving no tenant.
    /// </summary>
    [Fact]
    public async Task TheApplicationsStrategy_ReplacesTheGeneratorsDefault_RatherThanChainingAfterIt()
    {
        var services = Services();
        services.AddPragmaticMultiTenancy();                           // what the generated host calls
        services.AddPragmaticMultiTenancy(mt => mt.UseHeader());       // what UseMultiTenancy(...) calls

        services.Count(d => d.ServiceType == typeof(ITenantResolver)).Should().Be(1);
        (await ResolveWith(services, header: null, claim: null)).Should().BeNull();
    }

    /// <summary>The control: with only the generator's default, a host still resolves <c>"default"</c>.</summary>
    [Fact]
    public async Task OnlyTheGeneratorsDefault_ResolvesDefault()
    {
        var services = Services();
        services.AddPragmaticMultiTenancy();

        (await ResolveWith(services, header: "acme", claim: null)).Should().Be("default");
    }

    private static Task<string?> Resolve(Action<MultiTenancyBuilder> configure, string? header, string? claim)
    {
        var services = Services();
        services.AddPragmaticMultiTenancy(configure);
        return ResolveWith(services, header, claim);
    }

    private static async Task<string?> ResolveWith(IServiceCollection services, string? header, string? claim)
    {
        var context = new DefaultHttpContext();
        if (header is not null)
            context.Request.Headers["X-Tenant-Id"] = header;
        if (claim is not null)
            context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", claim)], "test"));

        services.AddSingleton<IHttpContextAccessor>(new HttpContextAccessor { HttpContext = context });

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<ITenantResolver>().ResolveAsync().ConfigureAwait(false);
    }

    private static IServiceCollection Services()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions();
        return services;
    }
}
