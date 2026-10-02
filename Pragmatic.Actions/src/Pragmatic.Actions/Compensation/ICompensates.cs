using Pragmatic.Result;

namespace Pragmatic.Actions.Compensation;

/// <summary>
///     Undoes what a successful action committed, given what that action returned.
/// </summary>
/// <typeparam name="TResult">The compensated action's return type.</typeparam>
/// <remarks>
///     <para>
///         A compensator is an ordinary scoped service, not an action: it holds the callee boundary's
///         repositories and writes through them, and the invoker that committed the original work is
///         the one that commits the undo. Making it an action instead would mean a second pipeline —
///         its own filters, its own permissions, its own telemetry — around a step the caller never
///         asked for and cannot authorise.
///     </para>
///     <para>
///         The type argument is what makes this checkable: <c>[UndoWith&lt;T&gt;]</c> only accepts
///         a <c>T</c> that compensates <i>this</i> action's return type, and PRAG0425 says so when it
///         does not.
///     </para>
/// </remarks>
public interface ICompensates<in TResult>
{
    /// <summary>Undoes the committed work described by <paramref name="committed" />.</summary>
    /// <param name="committed">What the compensated action returned when it succeeded.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success when the undo is staged; a failure is reported to the caller and logged.</returns>
    Task<VoidResult<IError>> Undo(TResult committed, CancellationToken ct = default);
}
