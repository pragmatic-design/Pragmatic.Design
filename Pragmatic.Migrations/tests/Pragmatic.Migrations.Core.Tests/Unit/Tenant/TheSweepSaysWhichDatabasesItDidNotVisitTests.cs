using System.Collections.Immutable;
using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Schema;
using Pragmatic.Migrations.Tenant;
using Pragmatic.MultiTenancy;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Migrations.Core.Tests.Unit.Tenant;

/// <summary>
///     A sweep reports the databases it did <b>not</b> touch, and which ones they were.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <c>TenantMigrationSummary</c> carried a <c>SkippedCount</c> and no list, so a run that
///         stopped early reported fewer lines than there were databases and nothing said which. And a
///         second kind of absence was not reported at all: the sweep visits <b>active</b> tenants, so an
///         organisation still provisioning, suspended, or left <c>Migrating</c> by an earlier failure has
///         a database that exists and was never looked at.
///     </para>
///     <para>
///         Both matter for the same reason: an operator running a migration across N customers needs the
///         run to account for N databases, not for however many it managed to reach. Casework had to
///         compute the second kind itself from <c>ITenantStore.GetAllAsync</c>, and that hand-written
///         query is what found a real case in its own suite.
///     </para>
///     <para>
///         ⚠️ The fake store here filters <c>GetActiveAsync</c> by state, unlike the one in
///         <see cref="TenantMigrationOrchestratorTests" />, which returns every tenant whatever its
///         state — so those cases never exercised the filter that produces this whole class of absence.
///     </para>
/// </remarks>
public class TheSweepSaysWhichDatabasesItDidNotVisitTests
{
    private static readonly SchemaVersion Desired = new(ImmutableArray<TableSchema>.Empty);
    private static readonly MigrationOptions Options = new();

    private static TenantInfo Tenant(string id, string? connectionString, TenantState state) => new()
    {
        TenantId = id,
        TenantName = id,
        ConnectionString = connectionString,
        State = state,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    /// <summary>The setpoint: a tenant with a database of its own that the sweep never visits.</summary>
    [Fact]
    public async Task ATenantThatIsNotActive_IsNamedWithItsReason()
    {
        var store = new FilteringTenantStore(
            Tenant("acme", "Host=h;Database=acme", TenantState.Active),
            Tenant("wayland", "Host=h;Database=wayland", TenantState.Provisioning),
            Tenant("shared-co", null, TenantState.Active));

        var summary = await new TenantMigrationOrchestrator(store, new AlwaysSucceeds())
            .MigrateAllTenantsAsync(Desired, Options);

        summary.TotalTenants.Should().Be(1, "only the active tenant with a database of its own is swept");

        summary.NotVisited.Should().ContainSingle()
            .Which.TenantId.Should().Be("wayland",
                "it has a database and the sweep does not visit it — the shared tenant has none, so it "
                + "is not an absence but a tenant with nothing of its own to migrate");

        summary.NotVisited[0].Reason.Should().Contain("Provisioning",
            "the reason says what to do about it, and the state is what an operator acts on");
    }

    /// <summary>
    ///     ⚠️ The control: nothing is listed when nothing was left out.
    /// </summary>
    /// <remarks>
    ///     "It reports what it skipped" is satisfied by reporting everything as skipped, which would make
    ///     every green run look like an incomplete one.
    /// </remarks>
    [Fact]
    public async Task WhenEverythingIsVisited_TheListIsEmpty()
    {
        var store = new FilteringTenantStore(
            Tenant("acme", "Host=h;Database=acme", TenantState.Active),
            Tenant("wayland", "Host=h;Database=wayland", TenantState.Active));

        var summary = await new TenantMigrationOrchestrator(store, new AlwaysSucceeds())
            .MigrateAllTenantsAsync(Desired, Options);

        summary.TotalTenants.Should().Be(2);
        summary.NotVisited.Should().BeEmpty();
    }

    /// <summary>
    ///     A run that stops on a failure names the tenants it never attempted, not just how many.
    /// </summary>
    /// <remarks>
    ///     This is the other kind of absence and the one <c>SkippedCount</c> already counted. Counting it
    ///     is what leaves an operator with "3 of 5 skipped" and no way to know which three.
    /// </remarks>
    [Fact]
    public async Task ARunThatStopsEarly_NamesWhatItNeverAttempted()
    {
        var store = new FilteringTenantStore(
            Tenant("first", "Host=h;Database=first", TenantState.Active),
            Tenant("second", "Host=h;Database=second", TenantState.Active),
            Tenant("third", "Host=h;Database=third", TenantState.Active));

        var orchestrator = new TenantMigrationOrchestrator(
            store, new FailsFor("first"), options: new TenantMigrationOptions { ContinueOnFailure = false });

        var summary = await orchestrator.MigrateAllTenantsAsync(Desired, Options);

        summary.SkippedCount.Should().Be(2, "the count is unchanged — this adds the names beside it");
        summary.NotVisited.Select(n => n.TenantId)
            .Should().BeEquivalentTo(["second", "third"]);
        summary.NotVisited[0].Reason.Should().Contain("stopped",
            "the reason distinguishes a run that gave up from a tenant the sweep does not visit");
    }

    private sealed class FilteringTenantStore(params TenantInfo[] tenants) : ITenantStore
    {
        private readonly List<TenantInfo> _tenants = [.. tenants];

        public Task<TenantInfo?> GetByIdAsync(string tenantId, CancellationToken ct = default)
            => Task.FromResult(_tenants.FirstOrDefault(t => t.TenantId == tenantId));

        public Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<TenantInfo>>(_tenants);

        /// <summary>⚠️ Actually filters, which is what produces the absences this class is about.</summary>
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
        public Task<MigrationResult> MigrateAsync(MigrationContext context, CancellationToken ct = default)
            => Task.FromResult(new MigrationResult(true, 1, TimeSpan.Zero, null, [], null));
    }

    private sealed class FailsFor(string tenantDatabase) : IMigrationRunner
    {
        public Task<MigrationResult> MigrateAsync(MigrationContext context, CancellationToken ct = default)
            => Task.FromResult(context.ConnectionString.Contains(tenantDatabase, StringComparison.Ordinal)
                ? new MigrationResult(false, 0, TimeSpan.Zero, null, [], "boom")
                : new MigrationResult(true, 1, TimeSpan.Zero, null, [], null));
    }
}
