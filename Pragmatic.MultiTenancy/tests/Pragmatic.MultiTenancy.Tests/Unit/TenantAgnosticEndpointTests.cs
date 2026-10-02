using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.MultiTenancy.Tests.Unit;

/// <summary>
///     The routes that belong to no tenant, and therefore may be asked without one.
/// </summary>
/// <remarks>
///     <para>
///         With <c>RequireTenant</c> — the default — the middleware refuses every request that
///         resolves no tenant. Right for the domain surface, wrong for everything underneath it.
///         Without these exceptions <c>/health</c> answers <b>400</b> without <c>X-Tenant-Id</c> and
///         <b>200</b> with it, so a liveness probe — which sends no such header, and has nowhere to get
///         one — would mark the application dead and restart it for ever, by a check that never ran.
///     </para>
///     <para>
///         The same applies to the published contract: it is what a caller fetches <em>before</em>
///         being anyone.
///     </para>
/// </remarks>
public class TenantAgnosticEndpointTests
{
    private sealed class StubResolver(string? tenantId) : ITenantResolver
    {
        public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(tenantId);
    }

    private static async Task<(int Status, bool ReachedNext, string? Resolved)> Run(
        string? tenantId, bool agnostic, ITenantStore? store = null, bool enforceState = false)
    {
        var reachedNext = false;
        var options = Options.Create(new MultiTenancyOptions
        {
            RequireTenant = true,
            EnforceTenantClaim = false,
            EnforceTenantState = enforceState,
        });

        var middleware = new TenantResolutionMiddleware(
            _ => { reachedNext = true; return Task.CompletedTask; },
            NullLogger<TenantResolutionMiddleware>.Instance,
            options);

        var context = new DefaultHttpContext();
        if (agnostic)
        {
            context.Features.Set<IEndpointFeature>(new EndpointFeature
            {
                Endpoint = new Endpoint(
                    _ => Task.CompletedTask,
                    new EndpointMetadataCollection(TenantAgnosticEndpoint.Instance),
                    "probe"),
            });
        }

        var tenantContext = new MutableTenantContext();
        await middleware.InvokeAsync(context, new StubResolver(tenantId), tenantContext, store)
            .ConfigureAwait(false);

        return (context.Response.StatusCode, reachedNext, tenantContext.TenantId);
    }

    private sealed class EndpointFeature : IEndpointFeature
    {
        public Endpoint? Endpoint { get; set; }
    }

    /// <summary>The shape that breaks a probe: no tenant, no endpoint metadata.</summary>
    [Fact]
    public async Task WithoutTheMarker_NoTenantIsStillRefused()
    {
        var (status, reachedNext, _) = await Run(tenantId: null, agnostic: false);

        status.Should().Be(StatusCodes.Status400BadRequest);
        reachedNext.Should().BeFalse("the domain surface is tenant-scoped and says so by refusing");
    }

    [Fact]
    public async Task WithTheMarker_NoTenantIsServed()
    {
        var (status, reachedNext, resolved) = await Run(tenantId: null, agnostic: true);

        reachedNext.Should().BeTrue("a liveness probe carries no tenant, and is not asking for one");
        status.Should().Be(StatusCodes.Status200OK);
        resolved.Should().BeNullOrEmpty();
    }

    /// <summary>
    ///     It relaxes the requirement, not the resolution.
    /// </summary>
    /// <remarks>
    ///     A tenant supplied on one of these routes is still resolved and still published downstream.
    ///     Skipping resolution outright would have been simpler and wrong: an endpoint that does not
    ///     <em>require</em> a tenant may still be glad of one — a health endpoint that reports per
    ///     tenant, a document that varies by it — and silently discarding what the caller sent is a
    ///     surprise nothing announces.
    /// </remarks>
    [Fact]
    public async Task WithTheMarker_ASuppliedTenantIsStillResolved()
    {
        var (_, reachedNext, resolved) = await Run(tenantId: "acme", agnostic: true);

        reachedNext.Should().BeTrue();
        resolved.Should().Be("acme");
    }

    /// <summary>
    ///     A suspended tenant does not make the host unreachable.
    /// </summary>
    /// <remarks>
    ///     The state check refuses with 403 for a tenant that is not Active. On a route that belongs to
    ///     no tenant that would be a strange answer: the state of one tenant is not a fact about
    ///     whether the process is alive, and an operator suspending a tenant would lose the probe.
    /// </remarks>
    [Fact]
    public async Task WithTheMarker_ASuspendedTenantDoesNotHideTheRoute()
    {
        var store = new InMemoryTenantStore();
        await store.CreateAsync(new TenantInfo
        {
            TenantId = "acme",
            TenantName = "acme",
            State = TenantState.Suspended,
            CreatedAt = DateTimeOffset.UnixEpoch,
        });

        var (status, reachedNext, _) = await Run("acme", agnostic: true, store, enforceState: true);

        reachedNext.Should().BeTrue();
        status.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task WithoutTheMarker_ASuspendedTenantIsStillRefused()
    {
        var store = new InMemoryTenantStore();
        await store.CreateAsync(new TenantInfo
        {
            TenantId = "acme",
            TenantName = "acme",
            State = TenantState.Suspended,
            CreatedAt = DateTimeOffset.UnixEpoch,
        });

        var (status, reachedNext, _) = await Run("acme", agnostic: false, store, enforceState: true);

        status.Should().Be(StatusCodes.Status403Forbidden);
        reachedNext.Should().BeFalse("the exemption is per endpoint, not a hole in the state check");
    }
}
