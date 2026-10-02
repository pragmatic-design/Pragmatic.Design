using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Pragmatic.MultiTenancy.Tests.Unit;

/// <summary>
///     <see cref="TenantResolutionMiddleware" /> must refuse a tenant the store reports as anything
///     other than <see cref="TenantState.Active" />.
/// </summary>
/// <remarks>
///     A state that is written and never read protects nothing. Suspending a tenant, deactivating it,
///     or catching one mid-provisioning must stop the requests — including the case the migration
///     orchestrator writes Suspended for, which is a tenant whose schema is in an indeterminate state.
/// </remarks>
public class TenantStateEnforcementTests
{
    private sealed class StubResolver(string? tenantId) : ITenantResolver
    {
        public ValueTask<string?> ResolveAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(tenantId);
    }

    private static TenantInfo Tenant(string id, TenantState state) => new()
    {
        TenantId = id,
        TenantName = id,
        State = state,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    /// <summary>Runs the middleware and reports the status code plus whether the pipeline continued.</summary>
    private static async Task<(int Status, bool ReachedNext)> Run(
        ITenantStore? store, bool enforceState = true, bool requireKnown = false)
    {
        var reachedNext = false;
        var options = Options.Create(new MultiTenancyOptions
        {
            EnforceTenantClaim = false,
            EnforceTenantState = enforceState,
            RequireKnownTenant = requireKnown,
        });
        var middleware = new TenantResolutionMiddleware(
            _ => { reachedNext = true; return Task.CompletedTask; },
            NullLogger<TenantResolutionMiddleware>.Instance,
            options);

        var context = new DefaultHttpContext();
        await middleware.InvokeAsync(context, new StubResolver("acme"), new MutableTenantContext(), store)
            .ConfigureAwait(false);

        return (context.Response.StatusCode, reachedNext);
    }

    private sealed class StubStore(TenantInfo? tenant) : ITenantStore
    {
        public Task<TenantInfo?> GetByIdAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(tenant);

        public Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantInfo>>(tenant is null ? [] : [tenant]);

        public Task<IReadOnlyList<TenantInfo>> GetActiveAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantInfo>>(
                tenant is { State: TenantState.Active } ? [tenant] : []);

        public Task<TenantInfo> CreateAsync(TenantInfo tenant, CancellationToken ct = default) => Task.FromResult(tenant);
        public Task<bool> UpdateAsync(TenantInfo tenant, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> DeactivateAsync(string tenantId, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> DeleteAsync(string tenantId, CancellationToken ct = default) => Task.FromResult(true);
    }

    [Theory]
    [InlineData(TenantState.Suspended)]
    [InlineData(TenantState.Deactivated)]
    [InlineData(TenantState.Provisioning)]
    [InlineData(TenantState.Migrating)]
    public async Task NonActiveTenant_IsRefusedWith403(TenantState state)
    {
        var (status, reachedNext) = await Run(new StubStore(Tenant("acme", state)));

        status.Should().Be(StatusCodes.Status403Forbidden);
        reachedNext.Should().BeFalse();
    }

    [Fact]
    public async Task ActiveTenant_IsServed()
    {
        var (_, reachedNext) = await Run(new StubStore(Tenant("acme", TenantState.Active)));

        reachedNext.Should().BeTrue();
    }

    /// <summary>
    ///     No store registered means no authority on state, not "assume suspended". Single-tenant and
    ///     resolver-only deployments must keep working.
    /// </summary>
    [Fact]
    public async Task WithNoTenantStore_TheRequestIsServed()
    {
        var (_, reachedNext) = await Run(store: null);

        reachedNext.Should().BeTrue();
    }

    /// <summary>The state check does not reject an unknown tenant: absent is not a state.</summary>
    [Fact]
    public async Task UnknownTenant_IsNotRefusedByTheStateCheck()
    {
        var (_, reachedNext) = await Run(new StubStore(null));

        reachedNext.Should().BeTrue();
    }

    /// <summary>
    ///     404 rather than 403: an unknown tenant is not a forbidden one, and answering "forbidden"
    ///     would confirm that the id names something.
    /// </summary>
    /// <summary>
    ///     The default is off, and that is deliberate: the generated host registers an EMPTY
    ///     InMemoryTenantStore, so rejecting unknown tenants by default would 404 every request of
    ///     every multi-tenant application.
    /// </summary>
    [Fact]
    public async Task UnknownTenant_WithTheDefaultOptions_IsServed()
    {
        var (_, reachedNext) = await Run(new StubStore(null), requireKnown: new MultiTenancyOptions().RequireKnownTenant);

        reachedNext.Should().BeTrue();
    }

    [Fact]
    public async Task UnknownTenant_WithRequireKnownTenant_IsRefusedWith404()
    {
        var (status, reachedNext) = await Run(new StubStore(null), requireKnown: true);

        status.Should().Be(StatusCodes.Status404NotFound);
        reachedNext.Should().BeFalse();
    }

    /// <summary>A known, active tenant still passes with the switch on.</summary>
    [Fact]
    public async Task KnownActiveTenant_WithRequireKnownTenant_IsServed()
    {
        var (_, reachedNext) = await Run(
            new StubStore(Tenant("acme", TenantState.Active)), requireKnown: true);

        reachedNext.Should().BeTrue();
    }

    /// <summary>
    ///     With no store there is no list to be absent from. Single-tenant and resolver-only
    ///     deployments must keep working even with the strictest switch on.
    /// </summary>
    [Fact]
    public async Task WithNoTenantStore_RequireKnownTenantDoesNotReject()
    {
        var (_, reachedNext) = await Run(store: null, requireKnown: true);

        reachedNext.Should().BeTrue();
    }

    /// <summary>
    ///     State and existence are separate switches: turning the state check off must not turn the
    ///     existence check off with it.
    /// </summary>
    [Fact]
    public async Task UnknownTenant_WithStateOffAndRequireKnownOn_IsStillRefused()
    {
        var (status, _) = await Run(new StubStore(null), enforceState: false, requireKnown: true);

        status.Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task WithEnforcementDisabled_ASuspendedTenantIsServed()
    {
        var (_, reachedNext) = await Run(
            new StubStore(Tenant("acme", TenantState.Suspended)), enforceState: false);

        reachedNext.Should().BeTrue();
    }
}
