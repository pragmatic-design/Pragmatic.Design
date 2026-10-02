using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Composition.ControlPlane;
using Pragmatic.Composition.Hosting;
using Pragmatic.ControlPlane;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Composition.Tests.Hosting;

/// <summary>
///     A drain takes the instance out of the rotation and leaves it running: it reports Draining,
///     keeps answering what still reaches it, and does not stop the process.
/// </summary>
/// <remarks>
///     Entering maintenance mode would make every request that still reaches it answer 503 while the
///     gateway has not yet heard, and stopping the application after the grace period would leave a
///     drained instance that cannot be put back. A drain is leaving the rotation; stopping for a deploy
///     is a separate step.
/// </remarks>
public sealed class ADrainLeavesTheRotationNotTheProcessTests
{
    private readonly LocalHostStatus _status = new();
    private readonly MaintenanceModeService _maintenance = new();
    private readonly RecordingControlPlane _controlPlane;

    public ADrainLeavesTheRotationNotTheProcessTests()
    {
        _status.TransitionTo(HostState.Ready);
        _controlPlane = new RecordingControlPlane(_status, _maintenance);
    }

    /// <summary>
    ///     It ends Drained, not Stopped: a drained instance stays up, to be put back or stopped by a deploy. The
    ///     handler does not take the application lifetime at all.
    /// </summary>
    [Fact]
    public async Task ADrain_DoesNotStopTheProcess_AndEndsDrained()
    {
        await DrainAsync();

        _status.State.Should().Be(HostState.Drained);
        _controlPlane.Reports.Select(report => report.State).Should().Equal([HostState.Draining, HostState.Drained]);
    }

    /// <summary>Put back: a drained instance is Ready again, and says so — which is what returns it to the rotation.</summary>
    [Fact]
    public async Task ExitMaintenance_PutsADrainedInstanceBack()
    {
        await DrainAsync();

        await new ExitMaintenanceCommandHandler(new MaintenanceHandleHolder(), _status, _controlPlane,
            NullLogger<ExitMaintenanceCommandHandler>.Instance).HandleAsync(new ExitMaintenanceCommand());

        _status.State.Should().Be(HostState.Ready);
        _controlPlane.Reports[^1].State.Should().Be(HostState.Ready);
    }

    /// <summary>The control: put back while still draining, it is not marked drained when the grace period ends.</summary>
    [Fact]
    public async Task PutBackWhileDraining_IsNotMarkedDrainedAfterwards()
    {
        var drain = new DrainCommandHandler(_status, _controlPlane, NullLogger<DrainCommandHandler>.Instance)
            .HandleAsync(new DrainCommand(TimeSpan.FromMilliseconds(300)));
        await new ExitMaintenanceCommandHandler(new MaintenanceHandleHolder(), _status, _controlPlane,
            NullLogger<ExitMaintenanceCommandHandler>.Instance).HandleAsync(new ExitMaintenanceCommand());

        await drain.ConfigureAwait(true);

        _status.State.Should().Be(HostState.Ready);
    }

    /// <summary>
    ///     What reaches it while the gateway has not yet heard is answered, not refused: maintenance mode is
    ///     never on during a drain.
    /// </summary>
    [Fact]
    public async Task ADrain_NeverAnswers503()
    {
        await DrainAsync();

        _controlPlane.Reports.Should().NotBeEmpty();
        _controlPlane.Reports.Should().OnlyContain(report => !report.MaintenanceOn);
    }

    /// <summary>The first thing the control plane hears is that the instance is draining: that is what takes it out of the rotation.</summary>
    [Fact]
    public async Task ADrain_ReportsDrainingFirst()
    {
        await DrainAsync();

        _controlPlane.Reports[0].State.Should().Be(HostState.Draining);
    }

    private Task DrainAsync()
        => new DrainCommandHandler(_status, _controlPlane, NullLogger<DrainCommandHandler>.Instance)
            .HandleAsync(new DrainCommand(TimeSpan.FromMilliseconds(50)));
}
