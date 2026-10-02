#pragma warning disable CA2007 // ConfigureAwait in test code

using System.Collections.Immutable;
using Pragmatic.Testing.Assertions;
using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Tenant;
using Pragmatic.MultiTenancy;

namespace Pragmatic.Migrations.Core.Tests.Unit.Tenant;

/// <summary>
///     <see cref="TenantMigrationOptions" /> was public, documented, and referenced by nothing —
///     the orchestrator never took it. These tests pin each option to an observable behaviour so it
///     cannot silently become decorative again.
/// </summary>
public class TenantMigrationOptionsTests
{
    private static readonly SchemaVersion Desired = new(ImmutableArray<TableSchema>.Empty);
    private static readonly MigrationOptions Options = new();

    private static TenantInfo Tenant(string id) => new()
    {
        TenantId = id,
        TenantName = id,
        ConnectionString = $"conn-{id}",
        State = TenantState.Active,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    private static MigrationResult Failure(string error) =>
        new(false, 0, TimeSpan.Zero, null, [], error);

    [Fact]
    public async Task ContinueOnFailure_False_StopsAtTheFirstFailure()
    {
        var store = new FakeStore(Tenant("a"), Tenant("b"), Tenant("c"));
        var runner = new RecordingRunner { ResultFor = cs => cs == "conn-a" ? Failure("boom") : MigrationResult.NoChanges };
        var sut = new TenantMigrationOrchestrator(store, runner, options: new TenantMigrationOptions
        {
            ContinueOnFailure = false
        });

        var summary = await sut.MigrateAllTenantsAsync(Desired, Options);

        runner.Attempted.Should().ContainSingle().Which.Should().Be("conn-a");
        summary.FailureCount.Should().Be(1);
        summary.SkippedCount.Should().Be(2, "the untouched tenants are skipped, not failed");
    }

    [Fact]
    public async Task ContinueOnFailure_True_AttemptsEveryTenant()
    {
        var store = new FakeStore(Tenant("a"), Tenant("b"), Tenant("c"));
        var runner = new RecordingRunner { ResultFor = cs => cs == "conn-a" ? Failure("boom") : MigrationResult.NoChanges };
        var sut = new TenantMigrationOrchestrator(store, runner, options: new TenantMigrationOptions
        {
            ContinueOnFailure = true
        });

        var summary = await sut.MigrateAllTenantsAsync(Desired, Options);

        runner.Attempted.Should().HaveCount(3);
        summary.FailureCount.Should().Be(1);
        summary.SuccessCount.Should().Be(2);
        summary.SkippedCount.Should().Be(0);
    }

    [Fact]
    public async Task SuspendOnFailure_False_LeavesTheTenantMigratingForARetry()
    {
        var store = new FakeStore(Tenant("a"));
        var runner = new RecordingRunner { ResultFor = _ => Failure("boom") };
        var sut = new TenantMigrationOrchestrator(store, runner, options: new TenantMigrationOptions
        {
            SuspendOnFailure = false
        });

        await sut.MigrateAllTenantsAsync(Desired, Options);

        store.StateOf("a").Should().Be(TenantState.Migrating);
    }

    [Fact]
    public async Task SuspendOnFailure_True_SuspendsTheTenant()
    {
        var store = new FakeStore(Tenant("a"));
        var runner = new RecordingRunner { ResultFor = _ => Failure("boom") };
        var sut = new TenantMigrationOrchestrator(store, runner, options: new TenantMigrationOptions
        {
            SuspendOnFailure = true
        });

        await sut.MigrateAllTenantsAsync(Desired, Options);

        store.StateOf("a").Should().Be(TenantState.Suspended);
    }

    [Fact]
    public async Task TenantTimeout_Elapsed_FailsThatTenantOnly()
    {
        var store = new FakeStore(Tenant("slow"), Tenant("fast"));
        var runner = new RecordingRunner
        {
            // "slow" never returns until its own timeout token fires.
            DelayFor = cs => cs == "conn-slow" ? Timeout.InfiniteTimeSpan : TimeSpan.Zero
        };
        var sut = new TenantMigrationOrchestrator(store, runner, options: new TenantMigrationOptions
        {
            TenantTimeout = TimeSpan.FromMilliseconds(150),
            ContinueOnFailure = true
        });

        var summary = await sut.MigrateAllTenantsAsync(Desired, Options);

        summary.FailureCount.Should().Be(1);
        summary.SuccessCount.Should().Be(1, "one slow tenant must not take the whole sweep down");
        summary.Results.Single(r => r.TenantId == "slow").Result.Error.Should().Contain("TenantTimeout");
    }

    [Fact]
    public async Task MaxParallelism_GreaterThanOne_MigratesConcurrentlyAndKeepsTenantOrder()
    {
        var store = new FakeStore(Tenant("a"), Tenant("b"), Tenant("c"), Tenant("d"));
        var runner = new RecordingRunner { DelayFor = _ => TimeSpan.FromMilliseconds(120) };
        var sut = new TenantMigrationOrchestrator(store, runner, options: new TenantMigrationOptions
        {
            MaxParallelism = 4
        });

        var summary = await sut.MigrateAllTenantsAsync(Desired, Options);

        summary.SuccessCount.Should().Be(4);
        runner.MaxConcurrent.Should().BeGreaterThan(1, "MaxParallelism > 1 must actually overlap migrations");
        summary.Results.Select(r => r.TenantId).Should().Equal("a", "b", "c", "d");
    }

    [Fact]
    public void MaxParallelism_BelowOne_IsClampedToOne()
    {
        new TenantMigrationOptions { MaxParallelism = 0 }.MaxParallelism.Should().Be(1);
        new TenantMigrationOptions { MaxParallelism = -5 }.MaxParallelism.Should().Be(1);
    }

    private sealed class RecordingRunner : IMigrationRunner
    {
        private readonly Lock _gate = new();
        private int _current;

        public Func<string, MigrationResult>? ResultFor { get; init; }
        public Func<string, TimeSpan>? DelayFor { get; init; }
        public List<string> Attempted { get; } = [];
        public int MaxConcurrent { get; private set; }

        public async Task<MigrationResult> MigrateAsync(MigrationContext context, CancellationToken ct = default)
        {
            lock (_gate)
            {
                Attempted.Add(context.ConnectionString);
                _current++;
                if (_current > MaxConcurrent) MaxConcurrent = _current;
            }

            try
            {
                var delay = DelayFor?.Invoke(context.ConnectionString) ?? TimeSpan.Zero;
                if (delay != TimeSpan.Zero)
                    await Task.Delay(delay, ct);

                ct.ThrowIfCancellationRequested();
                return ResultFor?.Invoke(context.ConnectionString) ?? MigrationResult.NoChanges;
            }
            finally
            {
                lock (_gate) _current--;
            }
        }
    }

    private sealed class FakeStore(params TenantInfo[] tenants) : ITenantStore
    {
        private readonly Dictionary<string, TenantInfo> _tenants =
            tenants.ToDictionary(t => t.TenantId, StringComparer.Ordinal);

        private readonly List<TenantInfo> _order = [.. tenants];

        public TenantState StateOf(string id) => _tenants[id].State;

        public Task<TenantInfo?> GetByIdAsync(string tenantId, CancellationToken ct = default) =>
            Task.FromResult(_tenants.TryGetValue(tenantId, out var t) ? t : null);

        public Task<IReadOnlyList<TenantInfo>> GetActiveAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TenantInfo>>([.. _order.Select(t => _tenants[t.TenantId])]);

        public Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken ct = default) => GetActiveAsync(ct);

        public Task<TenantInfo> CreateAsync(TenantInfo tenant, CancellationToken ct = default)
        {
            _tenants[tenant.TenantId] = tenant;
            _order.Add(tenant);
            return Task.FromResult(tenant);
        }

        public Task<bool> UpdateAsync(TenantInfo tenant, CancellationToken ct = default)
        {
            _tenants[tenant.TenantId] = tenant;
            return Task.FromResult(true);
        }

        public Task<bool> DeactivateAsync(string tenantId, CancellationToken ct = default)
        {
            if (!_tenants.TryGetValue(tenantId, out var tenant)) return Task.FromResult(false);
            _tenants[tenantId] = tenant with { State = TenantState.Suspended };
            return Task.FromResult(true);
        }
    }
}
