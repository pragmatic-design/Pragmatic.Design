namespace Pragmatic.Pipeline;

/// <summary>
///     Tracks whether the current execution context is an internal (system-initiated) call.
///     When <see cref="IsInternalCall" /> is true, authorization filters skip permission checks.
///     Event handlers automatically run as internal calls — the system reacting to domain events
///     should not be blocked by the HTTP user's permissions.
/// </summary>
/// <remarks>
///     <para>
///         <b>The bypass is deliberate, and the trust boundary is the process.</b> Nothing outside it
///         can enter internal-call mode: no header, claim, route or request body reaches this — the
///         only callers are the generated boundary invokers, the event dispatcher and the message
///         bus, all in-process. Code running inside the host is trusted by construction, so exposing
///         this on a public interface grants no authority that such code did not already have.
///     </para>
///     <para>
///         It is on a public interface, rather than internal to Pragmatic.Actions, so that Events and
///         Messaging can enter the mode without depending on Actions. Both call it conditionally
///         (<c>_callContext?.EnterInternalCall()</c>): in an application without Actions installed,
///         no implementation is registered and the mode simply does not exist.
///     </para>
/// </remarks>
public interface ICallContext
{
    /// <summary>
    ///     Whether the current invocation is an internal call (system-initiated, not user-initiated).
    /// </summary>
    bool IsInternalCall { get; }

    /// <summary>
    ///     Marks the start of an internal call. Returns a disposable that restores the previous state.
    ///     Supports nesting.
    /// </summary>
    IDisposable EnterInternalCall();
}
