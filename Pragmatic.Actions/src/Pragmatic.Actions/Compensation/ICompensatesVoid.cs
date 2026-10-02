using Pragmatic.Result;

namespace Pragmatic.Actions.Compensation;

/// <summary>
///     Undoes what a successful void action committed.
/// </summary>
/// <remarks>
///     The void counterpart of <see cref="ICompensates{TResult}" />: there is no result to describe the
///     committed work, so the compensator must find it another way — usually from the same inputs, read
///     back through its own repositories. The name carries the <c>Void</c> suffix because C# cannot
///     overload an interface on arity alone in a way <c>[UndoWith&lt;T&gt;]</c> could disambiguate.
/// </remarks>
public interface ICompensatesVoid
{
    /// <summary>Undoes the committed work.</summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Success when the undo is staged; a failure is reported to the caller and logged.</returns>
    Task<VoidResult<IError>> Undo(CancellationToken ct = default);
}
