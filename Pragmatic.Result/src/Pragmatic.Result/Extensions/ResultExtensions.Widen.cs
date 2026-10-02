using System.Runtime.CompilerServices;

namespace Pragmatic.Result.Extensions;

/// <summary>
///     Widening extensions that lift a result over a concrete error type to a result over the
///     <see cref="IError" /> abstraction.
/// </summary>
public static partial class ResultExtensions
{
    // =============================================================================
    // AsIError — widen the error type to IError
    // =============================================================================

    /// <summary>
    ///     Widens a <see cref="Result{TValue, TError}" /> over a concrete error type to a
    ///     <see cref="Result{TValue, TError}" /> over <see cref="IError" />, preserving the value on
    ///     success and the (now up-cast) error on failure.
    /// </summary>
    /// <typeparam name="TValue">The success value type.</typeparam>
    /// <typeparam name="TError">The concrete error type.</typeparam>
    /// <param name="result">The result to widen.</param>
    /// <returns>The same result with its error type widened to <see cref="IError" />.</returns>
    /// <remarks>
    ///     Use this on the most-travelled framework path — a Mutation or DomainAction that computes a
    ///     <c>Result&lt;T, ConcreteError&gt;</c> but must return the boundary-uniform
    ///     <c>Result&lt;T, IError&gt;</c>. It replaces the manual unwrap/rewrap
    ///     <c>Result&lt;T, IError&gt;.Failure(result.Error)</c>, which loses the success value and forces
    ///     a branch.
    /// </remarks>
    /// <example>
    ///     <code>
    /// public Result&lt;Reservation, IError&gt; Handle(ConfirmReservation command)
    /// {
    ///     Result&lt;Reservation, NotFoundError&gt; result = _repository.Confirm(command.Id);
    ///     return result.AsIError();
    /// }
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TValue, IError> AsIError<TValue, TError>(this Result<TValue, TError> result)
        where TError : IError
    {
        return result.MapError<IError>(static error => error);
    }

    /// <summary>
    ///     Widens a <see cref="VoidResult{TError}" /> over a concrete error type to a
    ///     <see cref="VoidResult{TError}" /> over <see cref="IError" />.
    /// </summary>
    /// <typeparam name="TError">The concrete error type.</typeparam>
    /// <param name="result">The result to widen.</param>
    /// <returns>The same result with its error type widened to <see cref="IError" />.</returns>
    /// <remarks>
    ///     The void counterpart of <see cref="AsIError{TValue, TError}(Result{TValue, TError})" />: use it
    ///     when a Mutation or DomainAction returns <c>VoidResult&lt;IError&gt;</c> but the inner call
    ///     produced a concrete <c>VoidResult&lt;ConcreteError&gt;</c>.
    /// </remarks>
    /// <example>
    ///     <code>
    /// public VoidResult&lt;IError&gt; Handle(DeleteReservation command)
    /// {
    ///     VoidResult&lt;NotFoundError&gt; result = _repository.Delete(command.Id);
    ///     return result.AsIError();
    /// }
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<IError> AsIError<TError>(this VoidResult<TError> result)
        where TError : IError
    {
        return result.MapError<IError>(static error => error);
    }
}
