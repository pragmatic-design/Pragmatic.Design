using Pragmatic.Testing.Assertions;
using Pragmatic.Agent.Platform;
using Xunit;

namespace Pragmatic.Agent.Tests.Platform;

public class KubernetesPlatformAdapterTests
{
    private readonly KubernetesPlatformAdapter _adapter = new();

    [Fact]
    public void Initially_IsReady()
    {
        _adapter.IsReady.Should().BeTrue();
    }

    [Fact]
    public async Task ActivateMaintenance_SetsNotReady()
    {
        await _adapter.ActivateMaintenanceAsync("app-1");
        _adapter.IsReady.Should().BeFalse();
    }

    [Fact]
    public async Task DeactivateMaintenance_SetsReady()
    {
        await _adapter.ActivateMaintenanceAsync("app-1");
        await _adapter.DeactivateMaintenanceAsync("app-1");
        _adapter.IsReady.Should().BeTrue();
    }

    [Fact]
    public async Task ReportHealth_Healthy_SetsReady()
    {
        await _adapter.ActivateMaintenanceAsync("app-1"); // not ready
        await _adapter.ReportHealthAsync("app-1", HealthStatus.Healthy);
        _adapter.IsReady.Should().BeTrue();
    }

    [Fact]
    public async Task ReportHealth_Unhealthy_SetsNotReady()
    {
        await _adapter.ReportHealthAsync("app-1", HealthStatus.Unhealthy);
        _adapter.IsReady.Should().BeFalse();
    }

    [Fact]
    public async Task IsAppRunning_AlwaysTrue_InSidecarMode()
    {
        var running = await _adapter.IsAppRunningAsync("app-1");
        running.Should().BeTrue();
    }
}
