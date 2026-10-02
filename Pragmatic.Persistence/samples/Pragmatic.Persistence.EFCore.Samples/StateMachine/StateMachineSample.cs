namespace Pragmatic.Persistence.EFCore.Samples.StateMachine;

/// <summary>
///     Demonstrates the generated state machine on <see cref="SupportTicket"/>:
///     <c>[StateMachine&lt;TEnum&gt;]</c> + <c>[InitialState]</c> + <c>[TransitionFrom]</c>
///     producing <c>CanTransitionTo</c>, <c>TransitionTo</c>, and <c>AllowedTransitions</c>.
/// </summary>
public static class StateMachineSample
{
    public static void Run()
    {
        Console.WriteLine("═══ State Machine ([StateMachine<TicketStatus>]) ═══");
        Console.WriteLine();

        var ticket = new SupportTicket { Subject = "Printer on fire" };
        Console.WriteLine($"  New ticket status     : {ticket.Status}");
        Console.WriteLine($"  Allowed from {ticket.Status,-10}: {Format(ticket.AllowedTransitions())}");
        Console.WriteLine();

        // ── Legal happy-path transitions ──
        Walk(ticket, TicketStatus.InProgress);
        Walk(ticket, TicketStatus.Resolved);
        Walk(ticket, TicketStatus.Closed);
        Console.WriteLine();

        // ── Illegal transition is rejected with a ConflictError (no throw) ──
        Console.WriteLine("  Attempt illegal move: Closed → InProgress");
        var canReopen = ticket.CanTransitionTo(TicketStatus.InProgress);
        var reopen = ticket.TransitionTo(TicketStatus.InProgress);
        Console.WriteLine($"    CanTransitionTo  : {canReopen} (expects False)");
        Console.WriteLine($"    TransitionTo     : {(reopen.IsSuccess ? "succeeded" : "rejected")} → {DescribeError(reopen)}");
        Console.WriteLine($"    Status unchanged : {ticket.Status} (expects Closed)");
        Console.WriteLine();

        // ── A second ticket showing the Cancelled branch (reachable from Open/InProgress) ──
        var cancelled = new SupportTicket { Subject = "Duplicate request" };
        var cancel = cancelled.TransitionTo(TicketStatus.Cancelled);
        Console.WriteLine($"  Second ticket: Open → Cancelled = {(cancel.IsSuccess ? "succeeded" : "rejected")} → status {cancelled.Status}");
        Console.WriteLine();
    }

    private static void Walk(SupportTicket ticket, TicketStatus target)
    {
        var from = ticket.Status;
        var result = ticket.TransitionTo(target);
        Console.WriteLine($"  {from,-10} → {target,-10}: {(result.IsSuccess ? "OK" : "REJECTED")}");
    }

    private static string Format(ReadOnlySpan<TicketStatus> states)
    {
        if (states.Length == 0) return "(none)";
        var parts = new string[states.Length];
        for (var i = 0; i < states.Length; i++) parts[i] = states[i].ToString();
        return string.Join(", ", parts);
    }

    private static string DescribeError(Pragmatic.Result.VoidResult<Pragmatic.Result.IError> result)
        => result.IsSuccess ? "(no error)" : result.Error.Title;
}
