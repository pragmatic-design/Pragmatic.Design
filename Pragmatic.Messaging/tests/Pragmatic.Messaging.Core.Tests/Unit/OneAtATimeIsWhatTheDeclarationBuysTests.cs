using Microsoft.Extensions.Logging.Abstractions;
using Pragmatic.Messaging.Attributes;
using Pragmatic.Testing.Assertions;

namespace Pragmatic.Messaging.Core.Tests.Unit;

/// <summary>
///     What <c>[ConcurrencyLimit]</c> does: a second execution of the same handler waits
///     while the first is inside, and runs when it leaves.
/// </summary>
/// <remarks>
///     <para>
///         This is the unit-level proof required before the attribute is written in an example. The gate is a <c>static</c> semaphore on the generated
///         pipeline, so what it controls is invisible from outside the process: an end-to-end test over
///         a broker can see that two messages were both handled and never that one waited.
///     </para>
///     <para>
///         ⚠️ <b>Choreographed, not hoped for.</b> The first execution is <em>held</em> by the test and
///         leaves only when told, so "the second is waiting" is a fact about a controlled state and not
///         about scheduling luck. The one thing that cannot be observed directly is a negative — that
///         the second has not entered — so the control calibrates it:
///         <see cref="WithoutTheDeclaration_TheSecondExecutionEntersWhileTheFirstIsStillInside" /> runs
///         the identical choreography on a handler with no attribute and watches the second enter in
///         milliseconds. A pause long enough to hide the difference would fail that one too.
///     </para>
/// </remarks>
public class OneAtATimeIsWhatTheDeclarationBuysTests
{
    /// <summary>How long a second execution is watched for before it is called "still waiting".</summary>
    /// <remarks>
    ///     Only ever used for the negative. The control reaches the same state without a declaration in
    ///     a fraction of it, which is what makes this number a measurement rather than a guess.
    /// </remarks>
    private static readonly TimeSpan LongEnoughToNotice = TimeSpan.FromMilliseconds(500);

    [Fact]
    public async Task WhileOneExecutionIsInside_ASecondWaitsOutside()
    {
        var gate = new Gate();
        var pipeline = new TheProvisioningThatRunsOneAtATime.Pipeline(
            new TheProvisioningThatRunsOneAtATime(gate),
            NullLogger<TheProvisioningThatRunsOneAtATime.Pipeline>.Instance,
            []);

        var first = pipeline.ExecuteAsync(new AProvisioningWasAsked("a"), MessageContext.New());
        await WaitUntilAsync(() => gate.Inside == 1, "the first execution reached the handler");

        var second = pipeline.ExecuteAsync(new AProvisioningWasAsked("b"), MessageContext.New());
        await Task.Delay(LongEnoughToNotice);

        gate.Inside.Should().Be(1,
            "the declaration is one at a time, and the first has not left yet");

        gate.LetThemThrough();
        await Task.WhenAll(first, second);

        gate.Inside.Should().Be(2,
            "and the gate lets the second through once the first leaves — a gate that never reopened "
            + "would keep the count at one for the same reason and would be a worse defect");
    }

    /// <summary>
    ///     The control: the identical choreography, on a handler that declares nothing.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Without it, the assertion above passes on a machine that simply did not get round to the
    ///     second execution — and on a pipeline that dropped it. Here the second enters while the first
    ///     is still held, which is both the calibration of the wait above and the proof that these two
    ///     executions really do overlap when nothing stops them.
    /// </remarks>
    [Fact]
    public async Task WithoutTheDeclaration_TheSecondExecutionEntersWhileTheFirstIsStillInside()
    {
        var gate = new Gate();
        var pipeline = new TheProvisioningThatRunsWhenItLikes.Pipeline(
            new TheProvisioningThatRunsWhenItLikes(gate),
            NullLogger<TheProvisioningThatRunsWhenItLikes.Pipeline>.Instance,
            []);

        var first = pipeline.ExecuteAsync(new AProvisioningWasAsked("a"), MessageContext.New());
        await WaitUntilAsync(() => gate.Inside == 1, "the first execution reached the handler");

        var second = pipeline.ExecuteAsync(new AProvisioningWasAsked("b"), MessageContext.New());

        await WaitUntilAsync(() => gate.Inside == 2,
            "with nothing declared the second execution enters while the first is still inside");

        gate.LetThemThrough();
        await Task.WhenAll(first, second);
    }

    /// <summary>Polls until <paramref name="until" /> holds, or fails saying what it was waiting for.</summary>
    private static async Task WaitUntilAsync(Func<bool> until, string what)
    {
        var deadline = DateTimeOffset.UtcNow + TimeSpan.FromSeconds(5);

        while (DateTimeOffset.UtcNow < deadline)
        {
            if (until())
                return;

            await Task.Delay(10).ConfigureAwait(false);
        }

        throw new InvalidOperationException($"Timed out waiting until {what}.");
    }
}

/// <summary>The message the two handlers below are asked to handle.</summary>
public sealed record AProvisioningWasAsked(string Organisation);

/// <summary>
///     A handler that stays inside until the test lets it out, and counts who got in.
/// </summary>
/// <remarks>
///     Shared by the declaration and its control so the two differ in exactly one thing: the attribute.
/// </remarks>
public sealed class Gate
{
    private readonly TaskCompletionSource _open = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _inside;

    /// <summary>How many executions have reached the handler body.</summary>
    public int Inside => Volatile.Read(ref _inside);

    /// <summary>Called from the handler: counts the entry and waits to be let out.</summary>
    public Task EnterAsync()
    {
        Interlocked.Increment(ref _inside);
        return _open.Task;
    }

    /// <summary>Lets every waiting execution finish.</summary>
    public void LetThemThrough() => _open.TrySetResult();
}

/// <summary>
///     The shape the declaration is for: provisioning a database is work one wants one of at a time,
///     whatever the broker delivers.
/// </summary>
[MessageHandler]
[ConcurrencyLimit(1)]
public sealed partial class TheProvisioningThatRunsOneAtATime(Gate gate)
    : IMessageHandler<AProvisioningWasAsked>
{
    public Task HandleAsync(AProvisioningWasAsked message, MessageContext context,
        CancellationToken ct = default)
        => gate.EnterAsync();
}

/// <summary>The same handler without the declaration — the control's subject.</summary>
[MessageHandler]
public sealed partial class TheProvisioningThatRunsWhenItLikes(Gate gate)
    : IMessageHandler<AProvisioningWasAsked>
{
    public Task HandleAsync(AProvisioningWasAsked message, MessageContext context,
        CancellationToken ct = default)
        => gate.EnterAsync();
}
