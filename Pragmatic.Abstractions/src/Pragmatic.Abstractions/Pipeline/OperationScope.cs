namespace Pragmatic.Pipeline;

/// <summary>
///     The business operation the current flow is executing, by name.
/// </summary>
/// <remarks>
///     <para>
///         The audit trail is written where the data changes — an interceptor over <c>SaveChanges</c> —
///         which knows the entity and the row but not why they changed. It recorded
///         <c>Data.EntityUpdated</c> on <c>Member</c>, while the Article 30 register named
///         <c>InviteMemberMutation</c>, and nothing joined the two. This is what joins them.
///     </para>
///     <para>
///         <b>Not <c>Activity.Current</c>, and the reason matters.</b> The invokers already start an
///         activity per operation, so the name appears to be available for free — but
///         <c>ActivitySource.StartActivity</c> returns <see langword="null" /> when no listener is
///         registered, so the name would be present only where tracing happens to be switched on. A field
///         in an audit trail that is populated in one deployment and empty in another is worse than one
///         that is never populated, because only the second is noticed.
///     </para>
///     <para>
///         Lives here rather than in <c>Pragmatic.Actions</c> so that persistence can read it without
///         depending on the layer above, the same arrangement <see cref="ICallContext" /> exists for.
///     </para>
/// </remarks>
public static class OperationScope
{
    // Flow-local, not scope-shared: parallel invocations in one DI scope — dispatching events, an
    // action fanning out — must not read each other's operation, or the trail attributes a change to
    // whichever branch happened to run last.
    private static readonly AsyncLocal<string?> CurrentName = new();
    private static readonly AsyncLocal<string?> CurrentCorrelation = new();

    /// <summary>
    ///     The operation being executed, fully qualified, or <see langword="null" /> outside one.
    /// </summary>
    /// <remarks>
    ///     Fully qualified because it is a join key: the register names operations that way, and a simple
    ///     name cannot distinguish two <c>ImportAction</c>s in two boundaries.
    /// </remarks>
    public static string? Current => CurrentName.Value;

    /// <summary>
    ///     An id shared by everything the outermost operation of the flow does, the operations nested in it included, or
    ///     <see langword="null" /> outside one (#750).
    /// </summary>
    /// <remarks>
    ///     What ties an operation's audit entries together where no tracing listener runs: the trace id is null there. Created when the first
    ///     operation of a flow is entered and dropped when it is left, so a nested operation keeps it and the next request gets another.
    /// </remarks>
    public static string? Correlation => CurrentCorrelation.Value;

    /// <summary>Declares the operation for the current flow until the returned handle is disposed.</summary>
    /// <param name="operation">
    ///     The operation type's full name. A blank name is ignored rather than recorded as one, so a
    ///     caller that cannot name itself leaves the outer operation standing instead of erasing it.
    /// </param>
    public static Scope Enter(string? operation)
        => string.IsNullOrWhiteSpace(operation)
            ? new Scope(CurrentName.Value, entered: false, startedCorrelation: false)
            : Set(operation!);

    private static Scope Set(string operation)
    {
        var previous = CurrentName.Value;
        CurrentName.Value = operation;

        var starts = CurrentCorrelation.Value is null;
        if (starts)
            CurrentCorrelation.Value = Guid.NewGuid().ToString("N");

        return new Scope(previous, entered: true, startedCorrelation: starts);
    }

    /// <summary>Restores the operation that was current before <see cref="Enter" />.</summary>
    /// <remarks>
    ///     Restores the captured value rather than clearing: an action that invokes a mutation is two
    ///     nested operations, and clearing on the inner one would leave the outer half unattributed. The correlation is dropped only by the
    ///     scope that created it.
    /// </remarks>
    public readonly struct Scope(string? previous, bool entered, bool startedCorrelation) : IDisposable
    {
        /// <inheritdoc />
        public void Dispose()
        {
            if (entered)
                CurrentName.Value = previous;
            if (startedCorrelation)
                CurrentCorrelation.Value = null;
        }
    }
}
