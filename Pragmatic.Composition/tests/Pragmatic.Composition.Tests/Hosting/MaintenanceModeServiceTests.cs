using Pragmatic.Testing.Assertions;
using Pragmatic.Composition.Hosting;

namespace Pragmatic.Composition.Tests.Hosting;

public class MaintenanceModeServiceTests
{
    [Fact]
    public void IsActive_Initially_ReturnsFalse()
    {
        var service = new MaintenanceModeService();

        service.IsActive.Should().BeFalse();
        service.Reason.Should().BeNull();
        service.ActivatedAt.Should().BeNull();
        service.EstimatedEnd.Should().BeNull();
    }

    [Fact]
    public void Activate_SetsIsActiveAndReason()
    {
        var service = new MaintenanceModeService();

        using var handle = service.Activate("Database migration");

        service.IsActive.Should().BeTrue();
        service.Reason.Should().Be("Database migration");
        service.ActivatedAt.Should().NotBeNull();
        service.ActivatedAt!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Activate_WithEta_SetsEstimatedEnd()
    {
        var service = new MaintenanceModeService();

        using var handle = service.Activate("Seeding", TimeSpan.FromMinutes(5));

        service.EstimatedEnd.Should().NotBeNull();
        service.EstimatedEnd!.Value.Should().BeCloseTo(
            DateTimeOffset.UtcNow.AddMinutes(5), TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Activate_WithoutEta_EstimatedEndIsNull()
    {
        var service = new MaintenanceModeService();

        using var handle = service.Activate("Quick fix");

        service.EstimatedEnd.Should().BeNull();
    }

    [Fact]
    public void Dispose_DeactivatesMaintenanceMode()
    {
        var service = new MaintenanceModeService();
        var handle = service.Activate("Test");

        handle.Dispose();

        service.IsActive.Should().BeFalse();
        service.Reason.Should().BeNull();
        service.ActivatedAt.Should().BeNull();
        service.EstimatedEnd.Should().BeNull();
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        var service = new MaintenanceModeService();
        var handle = service.Activate("Test");

        handle.Dispose();
        var act = () => handle.Dispose();

        act.Should().NotThrow();
        service.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Using_AutoDeactivates()
    {
        var service = new MaintenanceModeService();

        using (service.Activate("Temp"))
        {
            service.IsActive.Should().BeTrue();
        }

        service.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Activate_OverridesPreviousActivation()
    {
        var service = new MaintenanceModeService();
        using var first = service.Activate("First reason");

        using var second = service.Activate("Second reason");

        service.Reason.Should().Be("Second reason");
        service.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Activate_ConcurrentAccess_ThreadSafe()
    {
        var service = new MaintenanceModeService();
        var tasks = new List<Task>();

        for (var i = 0; i < 100; i++)
        {
            var reason = $"Task {i}";
            tasks.Add(Task.Run(() =>
            {
                using var handle = service.Activate(reason, TimeSpan.FromMinutes(1));
                // Access all properties to stress thread safety
                _ = service.IsActive;
                _ = service.Reason;
                _ = service.ActivatedAt;
                _ = service.EstimatedEnd;
            }));
        }

        await Task.WhenAll(tasks);

        // After all tasks complete, should be deactivated
        service.IsActive.Should().BeFalse();
    }

    [Fact]
    public void Activate_MultipleHandles_DisposingOne_StaysActiveUntilLast()
    {
        // Activation is ref-counted: two overlapping activations must not let one dispose turn
        // maintenance off while the other handle is still held.
        var service = new MaintenanceModeService();

        var h1 = service.Activate("deploy");
        var h2 = service.Activate("migrate");
        service.IsActive.Should().BeTrue();

        h2.Dispose();
        service.IsActive.Should().BeTrue("maintenance is still held by the first handle");

        h1.Dispose();
        service.IsActive.Should().BeFalse("the last handle was disposed");
    }
}
