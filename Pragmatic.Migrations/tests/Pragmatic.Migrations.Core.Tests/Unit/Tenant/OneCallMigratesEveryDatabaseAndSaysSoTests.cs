using System.Collections.Immutable;
using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Tenant;
using Pragmatic.MultiTenancy;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Migrations.Core.Tests.Unit.Tenant;

/// <summary>
///     One call brings the shared database and every tenant's to the schema, and returns a line about
///     each — including the ones nothing was done to.
/// </summary>
/// <remarks>
///     <para>
///         Both applications that needed this wrote the same twenty lines: the shared database through
///         <c>IMigrationRunner</c>, the dedicated ones through <c>ITenantMigrationOrchestrator</c>, a
///         line per result, and a query against the register for the tenants the sweep does not visit.
///     </para>
///     <para>
///         ⚠️ <b>Where and when it runs is still the application's</b> — there is no command and no host
///         mode, by the owner's decision: the moment to migrate N databases belongs to a deployment.
///         What is framework is <em>what</em> to do.
///     </para>
/// </remarks>
public class OneCallMigratesEveryDatabaseAndSaysSoTests
{
    private const string Shared = "Host=h;Database=app";
    private static readonly SchemaVersion Desired = new(ImmutableArray<TableSchema>.Empty);

    private static TenantInfo Tenant(string id, string? connectionString, TenantState state) => new()
    {
        TenantId = id,
        TenantName = id,
        ConnectionString = connectionString,
        State = state,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    private static IDatabaseMigrationSweep SweepOver(ITenantStore store, IMigrationRunner runner)
        => new DatabaseMigrationSweep(runner, new TenantMigrationOrchestrator(store, runner), store);

    /// <summary>
    ///     The setpoint: three tenants — two with a database of their own, one on the shared schema —
    ///     and a line per database.
    /// </summary>
    [Fact]
    public async Task EveryDatabase_GetsALineNamingIt()
    {
        var store = new Store(
            Tenant("acme", "Host=h;Database=acme", TenantState.Active),
            Tenant("wayland", "Host=h;Database=wayland", TenantState.Active),
            Tenant("shared-co", null, TenantState.Active));

        var report = await SweepOver(store, new AlwaysSucceeds()).RunAsync(Shared, Desired);

        report.Select(r => r.Database).Should().BeEquivalentTo(["app", "acme", "wayland"],
            "the shared database and the two dedicated ones — the tenant on the shared schema has no "
            + "database of its own, so it is not a line");

        report[0].Tenant.Should().BeNull("the shared database belongs to no tenant, and it comes first");
        report.Should().OnlyContain(r => r.Succeeded);
    }

    /// <summary>
    ///     ⚠️ A database the run did not visit still gets a line, saying why.
    /// </summary>
    /// <remarks>
    ///     The reason this exists: a run that reports only what it reached
    ///     is indistinguishable from one that reached everything. Reported as succeeded with zero
    ///     changes, because nothing went wrong — it just did not happen, and an operator has to be able
    ///     to tell those two apart.
    /// </remarks>
    [Fact]
    public async Task ADatabaseLeftBehind_IsALineWithItsReason()
    {
        var store = new Store(
            Tenant("acme", "Host=h;Database=acme", TenantState.Active),
            Tenant("wayland", "Host=h;Database=wayland", TenantState.Suspended));

        var report = await SweepOver(store, new AlwaysSucceeds()).RunAsync(Shared, Desired);

        var left = report.Should().ContainSingle(r => r.Tenant == "wayland").Subject;

        left.Database.Should().Be("wayland", "the report is about databases, so it names the one untouched");
        left.Changes.Should().Be(0);
        left.Error.Should().Contain("not visited").And.Contain("Suspended");
        left.Succeeded.Should().BeTrue("nothing failed — it was not attempted, which is a different thing");
    }

    /// <summary>
    ///     ⚠️ The control: a failure is a failure, and does not read as "left behind".
    /// </summary>
    /// <remarks>
    ///     Reporting everything as succeeded-with-a-note would make the run always look green, which is
    ///     the failure this whole report exists to make impossible.
    /// </remarks>
    [Fact]
    public async Task ADatabaseThatFailed_IsNotReportedAsSucceeded()
    {
        var store = new Store(Tenant("acme", "Host=h;Database=acme", TenantState.Active));

        var report = await SweepOver(store, new FailsFor("acme")).RunAsync(Shared, Desired);

        report.Should().ContainSingle(r => r.Tenant == "acme")
            .Which.Succeeded.Should().BeFalse();
    }

    /// <summary>The shared database is migrated first, because it holds the register.</summary>
    /// <remarks>
    ///     A tenant read afterwards would otherwise be answered by a database behind its own schema —
    ///     asserted on the order rather than argued, because the order is the whole of the reason.
    /// </remarks>
    [Fact]
    public async Task TheSharedDatabase_IsMigratedBeforeTheTenants()
    {
        var store = new Store(Tenant("acme", "Host=h;Database=acme", TenantState.Active));
        var runner = new AlwaysSucceeds();

        await SweepOver(store, runner).RunAsync(Shared, Desired);

        runner.Migrated.Should().StartWith([Shared]);
    }

    private sealed class Store(params TenantInfo[] tenants) : ITenantStore
    {
        private readonly List<TenantInfo> _tenants = [.. tenants];

        public Task<TenantInfo?> GetByIdAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(_tenants.FirstOrDefault(t => t.TenantId == tenantId));

        public Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantInfo>>(_tenants);

        public Task<IReadOnlyList<TenantInfo>> GetActiveAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantInfo>>(
                [.. _tenants.Where(t => t.State == TenantState.Active)]);

        public Task<TenantInfo> CreateAsync(TenantInfo tenant, CancellationToken ct = default)
        {
            _tenants.Add(tenant);
            return Task.FromResult(tenant);
        }

        public Task<bool> UpdateAsync(TenantInfo tenant, CancellationToken ct = default)
        {
            _tenants.RemoveAll(t => t.TenantId == tenant.TenantId);
            _tenants.Add(tenant);
            return Task.FromResult(true);
        }

        public Task<bool> DeleteAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(_tenants.RemoveAll(t => t.TenantId == tenantId) > 0);

        public Task<bool> DeactivateAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    private sealed class AlwaysSucceeds : IMigrationRunner
    {
        public List<string> Migrated { get; } = [];

        public Task<MigrationResult> MigrateAsync(MigrationContext context, CancellationToken ct = default)
        {
            Migrated.Add(context.ConnectionString);
            return Task.FromResult(new MigrationResult(true, 1, TimeSpan.Zero, null, [], null));
        }
    }

    private sealed class FailsFor(string database) : IMigrationRunner
    {
        public Task<MigrationResult> MigrateAsync(MigrationContext context, CancellationToken ct = default)
            => Task.FromResult(context.ConnectionString.Contains(database, StringComparison.Ordinal)
                ? new MigrationResult(false, 0, TimeSpan.Zero, null, [], "boom")
                : new MigrationResult(true, 1, TimeSpan.Zero, null, [], null));
    }
}
