// Pragmatic.Composition.Tests - ControlPlane runtime tests.
// Covers LocalHostStatus, HostHealthAggregator, ControlPlaneHealthCheck and NoOpControlPlane.

using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Pragmatic.Composition.ControlPlane;
using Pragmatic.ControlPlane;

namespace Pragmatic.Composition.Tests.Hosting;

public class LocalHostStatusTests
{
    [Fact]
    public void State_Initially_IsStarting()
    {
        var status = new LocalHostStatus();

        status.State.Should().Be(HostState.Starting);
        status.StateReason.Should().BeNull();
        status.MigrationStatus.Should().BeNull();
    }

    [Fact]
    public void TransitionTo_SetsStateAndReason()
    {
        var status = new LocalHostStatus();

        status.TransitionTo(HostState.Ready, "warmup complete");

        status.State.Should().Be(HostState.Ready);
        status.StateReason.Should().Be("warmup complete");
    }

    [Fact]
    public void TransitionTo_UpdatesStateChangedAt()
    {
        var status = new LocalHostStatus();
        var before = DateTimeOffset.UtcNow;

        status.TransitionTo(HostState.Ready);

        status.StateChangedAt.Should().BeOnOrAfter(before);
    }

    [Fact]
    public void UpdateMigrationProgress_WhileMigrating_SetsMigrationStatus()
    {
        var status = new LocalHostStatus();
        status.TransitionTo(HostState.Migrating);

        status.UpdateMigrationProgress(new MigrationStatus("main", 100, 42, 42, false, null));

        status.MigrationStatus.Should().NotBeNull();
        status.MigrationStatus!.DatabaseName.Should().Be("main");
        status.MigrationStatus.ProgressPercent.Should().Be(42);
    }

    [Fact]
    public void UpdateMigrationProgress_WhenNotMigrating_IsIgnored()
    {
        var status = new LocalHostStatus();
        status.TransitionTo(HostState.Ready);

        status.UpdateMigrationProgress(new MigrationStatus("main", 100, 10, 10, false, null));

        // Stale/out-of-order progress must not resurrect migration data outside Migrating state.
        status.MigrationStatus.Should().BeNull();
    }

    [Fact]
    public void TransitionTo_LeavingMigrating_ClearsMigrationStatus()
    {
        var status = new LocalHostStatus();
        status.TransitionTo(HostState.Migrating);
        status.UpdateMigrationProgress(new MigrationStatus("main", 100, 99, 99, false, null));

        status.TransitionTo(HostState.Ready);

        status.MigrationStatus.Should().BeNull();
    }
}

public class HostHealthAggregatorTests
{
    private sealed class StubContributor(string name, ContributorHealthReport report) : IHostHealthContributor
    {
        public string Name { get; } = name;
        public string? Category => null;
        public HealthContributorMode Mode => HealthContributorMode.Pull;
        public Task<ContributorHealthReport> CheckAsync(CancellationToken ct = default) => Task.FromResult(report);
    }

    private sealed class ThrowingContributor(string name) : IHostHealthContributor
    {
        public string Name { get; } = name;
        public string? Category => null;
        public HealthContributorMode Mode => HealthContributorMode.Pull;
        public Task<ContributorHealthReport> CheckAsync(CancellationToken ct = default)
            => throw new InvalidOperationException("boom");
    }

    [Fact]
    public async Task GetReportAsync_NoContributors_ReturnsHealthy()
    {
        var aggregator = new HostHealthAggregator([]);

        var report = await aggregator.GetReportAsync();

        report.OverallStatus.Should().Be(ContributorHealthStatus.Healthy);
        report.Contributors.Should().BeEmpty();
    }

    [Fact]
    public async Task GetReportAsync_AllHealthy_ReturnsHealthy()
    {
        var aggregator = new HostHealthAggregator(
        [
            new StubContributor("db", ContributorHealthReport.Healthy()),
            new StubContributor("cache", ContributorHealthReport.Healthy()),
        ]);

        var report = await aggregator.GetReportAsync();

        report.OverallStatus.Should().Be(ContributorHealthStatus.Healthy);
        report.Contributors.Should().ContainKeys("db", "cache");
    }

    [Fact]
    public async Task GetReportAsync_WorstStatusWins()
    {
        var aggregator = new HostHealthAggregator(
        [
            new StubContributor("db", ContributorHealthReport.Healthy()),
            new StubContributor("cache", ContributorHealthReport.Degraded("slow")),
            new StubContributor("queue", ContributorHealthReport.Unhealthy("down")),
        ]);

        var report = await aggregator.GetReportAsync();

        report.OverallStatus.Should().Be(ContributorHealthStatus.Unhealthy);
    }

    [Fact]
    public async Task GetReportAsync_ContributorThrows_ReportedAsUnhealthy()
    {
        var aggregator = new HostHealthAggregator([new ThrowingContributor("flaky")]);

        var report = await aggregator.GetReportAsync();

        report.OverallStatus.Should().Be(ContributorHealthStatus.Unhealthy);
        report.Contributors["flaky"].Status.Should().Be(ContributorHealthStatus.Unhealthy);
        report.Contributors["flaky"].Message.Should().Contain("boom");
    }
}

public class ControlPlaneHealthCheckTests
{
    private sealed class FixedStatus(HostState state, MigrationStatus? migration = null) : IHostStatus
    {
        public HostState State { get; } = state;
        public string? StateReason => null;
        public DateTimeOffset StateChangedAt => DateTimeOffset.UtcNow;
        public MigrationStatus? MigrationStatus { get; } = migration;
        public void TransitionTo(HostState newState, string? reason = null) { }
        public void UpdateMigrationProgress(MigrationStatus status) { }
    }

    [Fact]
    public async Task CheckHealthAsync_Starting_ReturnsUnhealthy()
    {
        var check = new ControlPlaneHealthCheck(new FixedStatus(HostState.Starting));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_Stopped_ReturnsUnhealthy()
    {
        var check = new ControlPlaneHealthCheck(new FixedStatus(HostState.Stopped));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Fact]
    public async Task CheckHealthAsync_Ready_ReturnsHealthy()
    {
        var check = new ControlPlaneHealthCheck(new FixedStatus(HostState.Ready));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Healthy);
        result.Data.Should().ContainKey("hostState");
    }

    [Fact]
    public async Task CheckHealthAsync_Maintenance_ReturnsDegraded()
    {
        var check = new ControlPlaneHealthCheck(new FixedStatus(HostState.Maintenance));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
    }

    [Fact]
    public async Task CheckHealthAsync_Migrating_IncludesMigrationData()
    {
        var migration = new MigrationStatus("billing", 100, 50, 50, false, null);
        var check = new ControlPlaneHealthCheck(new FixedStatus(HostState.Migrating, migration));

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        result.Status.Should().Be(HealthStatus.Degraded);
        result.Data.Should().ContainKey("migrationDatabase");
        result.Data["migrationDatabase"].Should().Be("billing");
    }
}

public class NoOpControlPlaneTests
{
    private sealed class FixedIdentity : IHostIdentity
    {
        public string HostId => "host-1";
        public string HostName => "test-host";
        public HostType HostType => HostType.Tenant;
        public DateTimeOffset StartedAt => DateTimeOffset.UtcNow;
    }

    private sealed class FixedStatus : IHostStatus
    {
        public HostState State => HostState.Ready;
        public string? StateReason => null;
        public DateTimeOffset StateChangedAt => DateTimeOffset.UtcNow;
        public MigrationStatus? MigrationStatus => null;
        public void TransitionTo(HostState newState, string? reason = null) { }
        public void UpdateMigrationProgress(MigrationStatus status) { }
    }

    [Fact]
    public void IsConnected_IsFalse()
    {
        var plane = new NoOpControlPlane(new FixedIdentity(), new FixedStatus());

        plane.IsConnected.Should().BeFalse();
    }

    [Fact]
    public async Task GetAllHostsAsync_ReturnsOnlySelf()
    {
        var plane = new NoOpControlPlane(new FixedIdentity(), new FixedStatus());

        var hosts = await plane.GetAllHostsAsync();

        hosts.Should().ContainSingle();
        hosts[0].HostId.Should().Be("host-1");
        hosts[0].HostName.Should().Be("test-host");
        hosts[0].State.Should().Be(HostState.Ready);
    }

    [Fact]
    public async Task SendCommandAsync_ReturnsNotConnectedError()
    {
        var plane = new NoOpControlPlane(new FixedIdentity(), new FixedStatus());

        var error = await plane.SendCommandAsync("other", new EnterMaintenanceCommand("test"));

        // Non-null return = command rejected; NoOp has no transport.
        error.Should().NotBeNull();
    }

    [Fact]
    public void Constructor_NullIdentity_Throws()
    {
        var act = () => new NoOpControlPlane(null!, new FixedStatus());

        act.Should().Throw<ArgumentNullException>().WithParameterName("identity");
    }

    [Fact]
    public void Constructor_NullStatus_Throws()
    {
        var act = () => new NoOpControlPlane(new FixedIdentity(), null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("status");
    }
}

public class MaintenanceHandleHolderTests
{
    [Fact]
    public void TrySet_FirstTime_ReturnsTrue()
    {
        var holder = new MaintenanceHandleHolder();

        holder.TrySet(new DummyHandle()).Should().BeTrue();
        holder.HasActiveHandle.Should().BeTrue();
    }

    [Fact]
    public void TrySet_WhenAlreadySet_ReturnsFalse()
    {
        var holder = new MaintenanceHandleHolder();
        holder.TrySet(new DummyHandle());

        holder.TrySet(new DummyHandle()).Should().BeFalse();
    }

    [Fact]
    public void TakeHandle_ReturnsAndClears()
    {
        var holder = new MaintenanceHandleHolder();
        var handle = new DummyHandle();
        holder.TrySet(handle);

        var taken = holder.TakeHandle();

        taken.Should().BeSameAs(handle);
        holder.HasActiveHandle.Should().BeFalse();
    }

    [Fact]
    public void TakeHandle_WhenEmpty_ReturnsNull()
    {
        var holder = new MaintenanceHandleHolder();

        holder.TakeHandle().Should().BeNull();
    }

    private sealed class DummyHandle : IDisposable
    {
        public void Dispose() { }
    }
}
