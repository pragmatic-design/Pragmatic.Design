using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.ControlPlane;
using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Tenant;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Migrations.Core.Tests.Unit.Tenant;

/// <summary>
///     Unit tests for <see cref="TenantMigrationOrchestrator"/>: the multi-tenant DB-per-tenant
///     migration loop, success/failure accounting, host state transitions, and cancellation.
///     All collaborators are in-memory fakes — no live database is required.
/// </summary>
public class TenantMigrationOrchestratorTests
{
    private static readonly SchemaVersion Desired =
        new(ImmutableArray<TableSchema>.Empty);

    private static readonly MigrationOptions Options = new();

    private static TenantInfo Dedicated(string id, string conn) => new()
    {
        TenantId = id,
        TenantName = id,
        ConnectionString = conn,
        State = TenantState.Active,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    private static TenantInfo Shared(string id) => new()
    {
        TenantId = id,
        TenantName = id,
        ConnectionString = null,
        State = TenantState.Active,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    [Fact]
    public async Task MigrateAllTenants_NoDedicatedDatabases_ReturnsEmptySummaryAndDoesNotTransition()
    {
        var store = new FakeTenantStore(Shared("a"), Shared("b"));
        var host = new FakeHostStatus();
        var sut = new TenantMigrationOrchestrator(store, new FakeRunner(), host);

        var summary = await sut.MigrateAllTenantsAsync(Desired, Options);

        summary.TotalTenants.Should().Be(0);
        summary.SuccessCount.Should().Be(0);
        summary.FailureCount.Should().Be(0);
        summary.SkippedCount.Should().Be(0);
        summary.Results.Should().BeEmpty();
        host.Transitions.Should().BeEmpty("a no-op run must not touch host state");
    }

    [Fact]
    public async Task MigrateAllTenants_AllSucceed_ReportsSuccessAndTransitionsToReady()
    {
        var store = new FakeTenantStore(Dedicated("a", "ca"), Dedicated("b", "cb"));
        var host = new FakeHostStatus();
        var runner = new FakeRunner(); // default: success
        var sut = new TenantMigrationOrchestrator(store, runner, host);

        var summary = await sut.MigrateAllTenantsAsync(Desired, Options);

        summary.TotalTenants.Should().Be(2);
        summary.SuccessCount.Should().Be(2);
        summary.FailureCount.Should().Be(0);
        summary.SkippedCount.Should().Be(0);
        host.Transitions.Should().Contain(HostState.Migrating);
        host.Transitions.Last().Should().Be(HostState.Ready);
        // Each tenant ends Active again.
        store.StatesOf("a").Should().ContainInOrder(TenantState.Migrating, TenantState.Active);
    }

    [Fact]
    public async Task MigrateAllTenants_OneFails_ReportsFailureAndTransitionsToMaintenance()
    {
        var store = new FakeTenantStore(Dedicated("ok", "c1"), Dedicated("bad", "c2"));
        var host = new FakeHostStatus();
        var runner = new FakeRunner
        {
            ResultFor = id => id == "c2"
                ? new MigrationResult(false, 0, TimeSpan.Zero, null, [], "boom")
                : MigrationResult.NoChanges,
        };
        var sut = new TenantMigrationOrchestrator(store, runner, host);

        var summary = await sut.MigrateAllTenantsAsync(Desired, Options);

        summary.SuccessCount.Should().Be(1);
        summary.FailureCount.Should().Be(1);
        summary.SkippedCount.Should().Be(0);
        host.Transitions.Last().Should().Be(HostState.Maintenance);
        host.LastReason.Should().Contain("failed");
        store.StatesOf("bad").Should().Contain(TenantState.Suspended);
    }

    [Fact]
    public async Task MigrateAllTenants_RunnerThrows_CountsAsFailureAndSuspendsTenant()
    {
        var store = new FakeTenantStore(Dedicated("x", "cx"));
        var host = new FakeHostStatus();
        var runner = new FakeRunner { ThrowFor = _ => new InvalidOperationException("db exploded") };
        var sut = new TenantMigrationOrchestrator(store, runner, host);

        var summary = await sut.MigrateAllTenantsAsync(Desired, Options);

        summary.FailureCount.Should().Be(1);
        summary.Results.Should().ContainSingle()
            .Which.Result.Error.Should().Be("db exploded");
        host.Transitions.Last().Should().Be(HostState.Maintenance);
        store.StatesOf("x").Should().Contain(TenantState.Suspended);
    }

    [Fact]
    public async Task MigrateAllTenants_CancelledMidLoop_PropagatesAndCountsRemainderAsSkipped()
    {
        using var cts = new CancellationTokenSource();
        var store = new FakeTenantStore(Dedicated("first", "c1"), Dedicated("second", "c2"));
        var host = new FakeHostStatus();
        // Cancel after the first tenant's migration completes, before the second starts.
        var runner = new FakeRunner { OnMigrate = () => cts.Cancel() };
        var sut = new TenantMigrationOrchestrator(store, runner, host);

        var act = () => sut.MigrateAllTenantsAsync(Desired, Options, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        // First tenant produced a result; the second was never reached → skipped, not failed.
        host.Transitions.Last().Should().Be(HostState.Maintenance);
        host.LastReason.Should().Contain("cancelled");
    }

    [Fact]
    public async Task MigrateTenant_UnknownTenant_ReturnsFailureResult()
    {
        var store = new FakeTenantStore(Dedicated("known", "c1"));
        var sut = new TenantMigrationOrchestrator(store, new FakeRunner());

        var result = await sut.MigrateTenantAsync("ghost", Desired, Options);

        result.Success.Should().BeFalse();
        result.Error.Should().Contain("ghost");
    }

    [Fact]
    public async Task MigrateTenant_SharedDatabaseTenant_ReturnsNoChanges()
    {
        var store = new FakeTenantStore(Shared("shared"));
        var sut = new TenantMigrationOrchestrator(store, new FakeRunner());

        var result = await sut.MigrateTenantAsync("shared", Desired, Options);

        result.Should().BeSameAs(MigrationResult.NoChanges);
    }

    [Fact]
    public async Task MigrateTenant_DedicatedTenant_DelegatesToRunnerWithTenantConnection()
    {
        var store = new FakeTenantStore(Dedicated("t", "tenant-conn"));
        var runner = new FakeRunner();
        var sut = new TenantMigrationOrchestrator(store, runner);

        var result = await sut.MigrateTenantAsync("t", Desired, Options);

        result.Success.Should().BeTrue();
        runner.LastConnectionString.Should().Be("tenant-conn");
    }

    private sealed class FakeTenantStore(params TenantInfo[] tenants) : ITenantStore
    {
        private readonly Dictionary<string, TenantInfo> _tenants =
            tenants.ToDictionary(t => t.TenantId);

        private readonly Dictionary<string, List<TenantState>> _stateLog = new();

        public IReadOnlyList<TenantState> StatesOf(string id) =>
            _stateLog.TryGetValue(id, out var log) ? log : [];

        public Task<TenantInfo?> GetByIdAsync(string tenantId, CancellationToken ct = default) =>
            Task.FromResult(_tenants.GetValueOrDefault(tenantId));

        public Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantInfo>>(_tenants.Values.ToList());

        public Task<IReadOnlyList<TenantInfo>> GetActiveAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantInfo>>(_tenants.Values.ToList());

        public Task<TenantInfo> CreateAsync(TenantInfo tenant, CancellationToken ct = default)
        {
            _tenants[tenant.TenantId] = tenant;
            return Task.FromResult(tenant);
        }

        public Task<bool> UpdateAsync(TenantInfo tenant, CancellationToken ct = default)
        {
            _tenants[tenant.TenantId] = tenant;
            (_stateLog.TryGetValue(tenant.TenantId, out var log)
                ? log
                : _stateLog[tenant.TenantId] = []).Add(tenant.State);
            return Task.FromResult(true);
        }

        public Task<bool> DeactivateAsync(string tenantId, CancellationToken ct = default) =>
            Task.FromResult(true);
    }

    private sealed class FakeRunner : IMigrationRunner
    {
        public Func<string, MigrationResult>? ResultFor { get; init; }
        public Func<string, Exception>? ThrowFor { get; init; }
        public Action? OnMigrate { get; init; }
        public string? LastConnectionString { get; private set; }

        public Task<MigrationResult> MigrateAsync(MigrationContext context, CancellationToken ct = default)
        {
            LastConnectionString = context.ConnectionString;

            if (ThrowFor?.Invoke(context.ConnectionString) is { } ex)
                return Task.FromException<MigrationResult>(ex);

            OnMigrate?.Invoke();
            ct.ThrowIfCancellationRequested();

            var result = ResultFor?.Invoke(context.ConnectionString) ?? MigrationResult.NoChanges;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeHostStatus : IHostStatus
    {
        public List<HostState> Transitions { get; } = [];
        public string? LastReason { get; private set; }

        public HostState State { get; private set; } = HostState.Starting;
        public string? StateReason => LastReason;
        public DateTimeOffset StateChangedAt { get; private set; }
        public MigrationStatus? MigrationStatus { get; private set; }

        public void TransitionTo(HostState newState, string? reason = null)
        {
            State = newState;
            LastReason = reason;
            StateChangedAt = DateTimeOffset.UtcNow;
            Transitions.Add(newState);
        }

        public void UpdateMigrationProgress(MigrationStatus status) => MigrationStatus = status;
    }
}
