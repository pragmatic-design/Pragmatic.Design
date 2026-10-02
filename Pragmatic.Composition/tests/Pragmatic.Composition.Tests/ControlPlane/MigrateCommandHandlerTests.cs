using System.Threading;
using System.Threading.Tasks;
using Pragmatic.Testing.Assertions;
using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Composition.ControlPlane;
using Pragmatic.ControlPlane;
using Xunit;

namespace Pragmatic.Composition.Tests.ControlPlane;

/// <summary>
///     FU2: <see cref="MigrateCommandHandler"/> delegates to the host migration coordinator when present,
///     and safely no-ops (with a log) when it is absent — replacing the former silent "no handler" no-op.
/// </summary>
public class MigrateCommandHandlerTests
{
    [Fact]
    public async Task HandleAsync_WithCoordinator_RunsMigrations()
    {
        var coordinator = new FakeCoordinator();
        var handler = new MigrateCommandHandler(NullLogger<MigrateCommandHandler>.Instance, coordinator);

        await handler.HandleAsync(new MigrateCommand()).ConfigureAwait(true);

        coordinator.Called.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_NoCoordinator_DoesNotThrow()
    {
        var handler = new MigrateCommandHandler(NullLogger<MigrateCommandHandler>.Instance, coordinator: null);

        var act = async () => await handler.HandleAsync(new MigrateCommand()).ConfigureAwait(true);

        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    private sealed class FakeCoordinator : IHostMigrationCoordinator
    {
        public bool Called { get; private set; }

        public Task MigrateAllAsync(CancellationToken ct = default)
        {
            Called = true;
            return Task.CompletedTask;
        }
    }
}
