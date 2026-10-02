namespace Pragmatic.Migrations.Cli.Commands;

/// <summary>
///     Bridges Ctrl+C (<see cref="Console.CancelKeyPress"/>) to a <see cref="CancellationToken"/>
///     so commands can cancel in-flight work cleanly instead of letting the process be killed.
/// </summary>
internal static class ConsoleCancellation
{
    /// <summary>
    ///     Creates a <see cref="CancellationTokenSource"/> that is cancelled on the first Ctrl+C.
    ///     The first press is intercepted (process keeps running so work can unwind); a second
    ///     press falls through to the default terminate behaviour.
    /// </summary>
    internal static CancellationTokenSource CreateLinkedTokenSource()
    {
        var cts = new CancellationTokenSource();

        ConsoleCancelEventHandler? handler = null;
        handler = (_, e) =>
        {
            if (!cts.IsCancellationRequested)
            {
                e.Cancel = true; // first press: cooperative cancel, keep the process alive
                cts.Cancel();
            }
            else
            {
                Console.CancelKeyPress -= handler; // second press: let the runtime terminate
            }
        };

        Console.CancelKeyPress += handler;
        cts.Token.Register(() => Console.CancelKeyPress -= handler);
        return cts;
    }
}
