// Pragmatic.Composition.Tests - MaintenanceModeService observer notification tests.
// Complements MaintenanceModeServiceTests with the AddObserver / observer-notification path and
// observer-exception isolation.

using Pragmatic.Testing.Assertions;
using Pragmatic.Composition.Hosting;
using Pragmatic.Maintenance;

namespace Pragmatic.Composition.Tests.Hosting;

public class MaintenanceModeServiceObserverTests
{
    private sealed class RecordingObserver : IMaintenanceModeObserver
    {
        private readonly TaskCompletionSource _activated = new();
        private readonly TaskCompletionSource _deactivated = new();

        public string? ActivatedReason { get; private set; }
        public TimeSpan? ActivatedEta { get; private set; }
        public Task Activated => _activated.Task;
        public Task Deactivated => _deactivated.Task;

        public Task OnActivatedAsync(string reason, TimeSpan? eta)
        {
            ActivatedReason = reason;
            ActivatedEta = eta;
            _activated.TrySetResult();
            return Task.CompletedTask;
        }

        public Task OnDeactivatedAsync()
        {
            _deactivated.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingObserver : IMaintenanceModeObserver
    {
        public Task OnActivatedAsync(string reason, TimeSpan? eta)
            => throw new InvalidOperationException("observer failure");

        public Task OnDeactivatedAsync()
            => throw new InvalidOperationException("observer failure");
    }

    [Fact]
    public async Task Activate_NotifiesRegisteredObserver()
    {
        var service = new MaintenanceModeService();
        var observer = new RecordingObserver();
        service.AddObserver(observer);

        using var handle = service.Activate("db migration", TimeSpan.FromMinutes(3));

        await observer.Activated.WaitAsync(TimeSpan.FromSeconds(5));
        observer.ActivatedReason.Should().Be("db migration");
        observer.ActivatedEta.Should().Be(TimeSpan.FromMinutes(3));
    }

    [Fact]
    public async Task Dispose_NotifiesObserverOfDeactivation()
    {
        var service = new MaintenanceModeService();
        var observer = new RecordingObserver();
        service.AddObserver(observer);

        var handle = service.Activate("temp");
        handle.Dispose();

        await observer.Deactivated.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Activate_ObserverThrows_DoesNotPropagateAndStaysActive()
    {
        var service = new MaintenanceModeService();
        service.AddObserver(new ThrowingObserver());

        // A failing observer is caught + logged internally; Activate must not throw and the
        // maintenance state must still be applied.
        var act = () => service.Activate("with throwing observer");

        act.Should().NotThrow();
        service.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task Activate_MultipleObservers_AllNotified()
    {
        var service = new MaintenanceModeService();
        var first = new RecordingObserver();
        var second = new RecordingObserver();
        service.AddObserver(first);
        service.AddObserver(second);

        using var handle = service.Activate("broadcast");

        await Task.WhenAll(first.Activated, second.Activated).WaitAsync(TimeSpan.FromSeconds(5));
        first.ActivatedReason.Should().Be("broadcast");
        second.ActivatedReason.Should().Be("broadcast");
    }
}
