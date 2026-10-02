using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Composition.ControlPlane;
using Pragmatic.Composition.Hosting;
using Pragmatic.ControlPlane;
using Pragmatic.Testing.Assertions;
using Xunit;

namespace Pragmatic.Composition.Tests.ControlPlane;

/// <summary>
///     The command handler is registered on every generated host, so this is the door an operator
///     drains traffic through from the outside. These pin who may open it.
/// </summary>
public class MaintenanceCommandHandlerTests
{
    private static (MaintenanceCommandHandler Handler, MaintenanceModeService Mode) Build(bool runtimeEnabled)
    {
        var mode = new MaintenanceModeService(NullLogger<MaintenanceModeService>.Instance);
        var options = new MaintenanceModeOptions { EnableRuntimeMaintenance = runtimeEnabled };
        var handler = new MaintenanceCommandHandler(
            mode, new MaintenanceHandleHolder(), options,
            NullLogger<MaintenanceCommandHandler>.Instance);

        return (handler, mode);
    }

    /// <summary>
    ///     A host that never called <c>UseMaintenanceMode()</c> has no maintenance page and no progress
    ///     stream, so draining it would answer every caller with a bare 503 and nothing behind it.
    /// </summary>
    [Fact]
    public async Task EnterMaintenance_WhenRuntimeMaintenanceIsNotEnabled_IsRefused()
    {
        var (handler, mode) = Build(runtimeEnabled: false);

        await handler.HandleAsync(new EnterMaintenanceCommand("migrating", null));

        mode.IsActive.Should().BeFalse(
            "a host that did not opt into maintenance must not be drained from the outside");
    }

    /// <summary>And a host that did opt in is drained, which is the whole point of the command.</summary>
    [Fact]
    public async Task EnterMaintenance_WhenRuntimeMaintenanceIsEnabled_Activates()
    {
        var (handler, mode) = Build(runtimeEnabled: true);

        await handler.HandleAsync(new EnterMaintenanceCommand("migrating", null));

        mode.IsActive.Should().BeTrue("UseMaintenanceMode() is the opt-in, and it was given");
        mode.Reason.Should().Be("migrating");
    }

    /// <summary>
    ///     The guard is not a substitute for idempotency: a second command on an opted-in host still
    ///     has to be a no-op rather than a second handle.
    /// </summary>
    [Fact]
    public async Task EnterMaintenance_Twice_StaysActiveAndKeepsTheFirstReason()
    {
        var (handler, mode) = Build(runtimeEnabled: true);

        await handler.HandleAsync(new EnterMaintenanceCommand("first", null));
        await handler.HandleAsync(new EnterMaintenanceCommand("second", null));

        mode.IsActive.Should().BeTrue();
        mode.Reason.Should().Be("first");
    }
}
