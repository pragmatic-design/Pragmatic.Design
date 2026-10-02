using Pragmatic.Pipeline;

namespace Pragmatic.Actions.Pipeline;

/// <summary>
///     Scoped context that tracks whether the current action invocation is an internal call
///     (i.e., one action calling another within the same boundary, or a system reaction to an event).
///     When <see cref="IsInternalCall"/> is true, authorization filters skip permission checks.
/// </summary>
/// <remarks>
///     Registered as scoped in DI. Supports nesting via <see cref="EnterInternalCall"/>.
///     The <see cref="ICallContext"/> interface allows infrastructure components (e.g., event dispatcher)
///     to enter internal call mode without depending on Pragmatic.Actions.
/// </remarks>
public sealed class ActionCallContext : ICallContext
{
    // Internal-call depth is flow-local, NOT scope-shared: parallel action flows in the same DI
    // scope (e.g. parallel event dispatch) must not leak IsInternalCall across each other, or an
    // external call could skip permission/policy checks. AsyncLocal scopes the depth to the async
    // flow that entered it; the value is captured per branch so concurrent flows stay isolated.
    private readonly AsyncLocal<int> _depth = new();

    /// <summary>
    ///     Whether the current invocation is an internal (intra-boundary) call.
    /// </summary>
    public bool IsInternalCall => _depth.Value > 0;

    /// <summary>
    ///     Marks the start of an internal call. Returns a disposable that resets the flag.
    ///     Supports nesting (multiple internal calls deep).
    /// </summary>
    public InternalCallScope EnterInternalCall()
    {
        var previous = _depth.Value;
        _depth.Value = previous + 1;
        return new InternalCallScope(this, previous);
    }

    /// <inheritdoc />
    IDisposable ICallContext.EnterInternalCall() => EnterInternalCall();

    /// <summary>
    ///     Disposable scope that restores the internal call depth on dispose.
    /// </summary>
    public readonly struct InternalCallScope(ActionCallContext context, int previousDepth) : IDisposable
    {
        // Restore the depth this flow had before entering, rather than blindly decrementing:
        // AsyncLocal flows forward into child branches but mutations in a child do not flow back
        // to the parent, so restoring the captured value is the correct, branch-safe reset.
        public void Dispose() => context._depth.Value = previousDepth;
    }
}
