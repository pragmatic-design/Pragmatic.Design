using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Pragmatic.Result;

/// <summary>
///     Represents the result of a void operation that can fail with a typed error.
/// </summary>
/// <remarks>
///     <para>
///         Use when an operation returns no value but can fail with a specific error type.
///         Zero-allocation struct with sub-nanosecond performance.
///     </para>
///     <para>
///         <b>Why VoidResult instead of UnitResult:</b>
///         "Void" is familiar to C# developers, avoids introducing artificial Unit type,
///         and has clear semantics: "operation completed, no value returned".
///     </para>
///     <para>
///         <b>Invariant — <c>default(VoidResult&lt;TError&gt;)</c> is NOT a valid value.</b>
///         A VoidResult MUST be created via <see cref="Success"/> or <see cref="Failure"/>. The default
///         (uninitialized) struct has <c>IsSuccess == false</c> and a <c>null</c> error, which is neither
///         a real success nor a real failure. Because every genuine failure carries a non-null error
///         (<see cref="Failure"/> rejects null), the default state is detected as "failure with null error"
///         and is treated as uninitialized: <see cref="TryGetError"/> returns <c>false</c> (it never hands
///         out a null error as if it were a failure), and reading <see cref="Error"/> throws an
///         <see cref="InvalidOperationException"/>. This prevents a default VoidResult from silently
///         propagating as <c>Failure(null)</c>.
///     </para>
/// </remarks>
/// <typeparam name="TError">The type of the error, must implement IError</typeparam>
public readonly struct VoidResult<TError> : IResultBase, IEquatable<VoidResult<TError>>
    where TError : IError
{
    private readonly TError? _error;

    private VoidResult(TError? error, bool isSuccess)
    {
        _error = error;
        IsSuccess = isSuccess;
    }

    /// <summary>
    ///     Gets whether the operation succeeded.
    /// </summary>
    public bool IsSuccess
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    }

    /// <summary>
    ///     Gets whether the operation failed.
    /// </summary>
    public bool IsFailure
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => !IsSuccess;
    }

    /// <summary>
    ///     Gets the error.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if IsSuccess is true, or if the VoidResult is uninitialized (a <c>default</c> struct that
    ///     was not created via <see cref="Success"/>/<see cref="Failure"/>, and therefore carries a null
    ///     error).
    /// </exception>
    /// <remarks>
    ///     Use only when IsFailure is true.
    ///     For safer access without exceptions, use TryGetError() or Match().
    /// </remarks>
    public TError Error
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            // A genuine failure always carries a non-null error (Failure rejects null). A null error here
            // means either success or an uninitialized default(VoidResult) — both are invalid Error access.
            if (_error is null)
                ThrowErrorAccessInvalid();
            return _error;
        }
    }

    // =============================================================================
    // IResultBase Implementation (for runtime identification)
    // =============================================================================

    /// <inheritdoc />
    bool IResultBase.HasValueType => false; // VoidResult has no value type

    /// <inheritdoc />
    object? IResultBase.ValueAsObject => null; // VoidResult has no value

    /// <inheritdoc />
    IError? IResultBase.ErrorAsObject => IsSuccess ? null : _error;

    // =============================================================================
    // Safe Access (non-throwing)
    // =============================================================================

    /// <summary>
    ///     Attempts to get the error without throwing.
    /// </summary>
    /// <param name="error">The error if this is a genuine failure; otherwise, default.</param>
    /// <returns>
    ///     True if the result is a genuine failure carrying a non-null error; otherwise, false.
    ///     An uninitialized <c>default(VoidResult)</c> returns false (it does NOT hand out a null error).
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetError([MaybeNullWhen(false)] out TError error)
    {
        // Gate on the error itself, not on IsSuccess: a default(VoidResult) has IsSuccess == false but a
        // null error, and must never be surfaced as a failure with a null error.
        error = _error;
        return _error is not null;
    }

    // =============================================================================
    // Factory Methods
    // =============================================================================

    /// <summary>
    ///     Creates a successful void result.
    /// </summary>
    /// <returns>A successful void result</returns>
    /// <example>
    ///     <code>
    /// public VoidResult&lt;NotFoundError&gt; Delete(Guid id)
    /// {
    ///     if (!_store.Remove(id))
    ///         return NotFoundError.For("Item", id);
    ///     return VoidResult&lt;NotFoundError&gt;.Success();
    /// }
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> Success()
    {
        return new VoidResult<TError>(default!, true);
    }

    /// <summary>
    ///     Creates a failed void result with an error.
    /// </summary>
    /// <param name="error">The error (cannot be null)</param>
    /// <returns>A failed void result</returns>
    /// <exception cref="ArgumentNullException">Thrown if error is null.</exception>
    /// <example>
    ///     <code>
    /// return VoidResult&lt;NotFoundError&gt;.Failure(NotFoundError.For("Item", id));
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult<TError> Failure(TError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new VoidResult<TError>(error, false);
    }

    // =============================================================================
    // Pattern Matching
    // =============================================================================

    /// <summary>
    ///     Matches the result state and executes the corresponding function.
    /// </summary>
    /// <typeparam name="TResult">The result type</typeparam>
    /// <param name="onSuccess">Function to execute if operation succeeded</param>
    /// <param name="onFailure">Function to execute if operation failed</param>
    /// <returns>The result of the executed function</returns>
    /// <example>
    ///     <code>
    /// VoidResult&lt;NotFoundError&gt; result = Delete(id);
    /// IResult response = result.Match(
    ///     onSuccess: () => Results.NoContent(),
    ///     onFailure: error => Results.NotFound(error.Title));
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TResult Match<TResult>(
        Func<TResult> onSuccess,
        Func<TError, TResult> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess ? onSuccess() : onFailure(_error!);
    }

    /// <summary>
    ///     Executes an action based on the result state.
    /// </summary>
    /// <param name="onSuccess">Action to execute if operation succeeded</param>
    /// <param name="onFailure">Action to execute if operation failed</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Match(
        Action onSuccess,
        Action<TError> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        if (IsSuccess)
            onSuccess();
        else
            onFailure(_error!);
    }

    /// <summary>
    ///     Asynchronously matches on success or failure, returning the result of the matching function.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<TResult> MatchAsync<TResult>(
        Func<Task<TResult>> onSuccess,
        Func<TError, Task<TResult>> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess
            ? onSuccess()
            : onFailure(_error!);
    }

    /// <summary>
    ///     Asynchronously matches on success or failure, executing the matching action.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task MatchAsync(
        Func<Task> onSuccess,
        Func<TError, Task> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess
            ? onSuccess()
            : onFailure(_error!);
    }

    // =============================================================================
    // Side-Effect Operations (Tap, OnSuccess, OnFailure)
    // =============================================================================

    /// <summary>
    ///     Executes an action on success without changing the result.
    /// </summary>
    /// <param name="action">Action to execute if successful</param>
    /// <returns>The same result, unchanged</returns>
    /// <remarks>
    ///     Use for side effects like logging or metrics.
    ///     The action is only executed if the result is successful.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VoidResult<TError> Tap(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (IsSuccess)
            action();

        return this;
    }

    /// <summary>
    ///     Executes an action on success without changing the result.
    ///     Alias for <see cref="Tap" />.
    /// </summary>
    /// <param name="action">Action to execute if successful</param>
    /// <returns>The same result, unchanged</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VoidResult<TError> OnSuccess(Action action)
    {
        return Tap(action);
    }

    /// <summary>
    ///     Executes an action on the error without changing the result.
    /// </summary>
    /// <param name="action">Action to execute if failed</param>
    /// <returns>The same result, unchanged</returns>
    /// <remarks>
    ///     Use for side effects like logging errors or metrics.
    ///     The action is only executed if the result is a failure.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VoidResult<TError> OnFailure(Action<TError> action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!IsSuccess)
            action(_error!);

        return this;
    }

    // =============================================================================
    // Error Recovery (OrElse)
    // =============================================================================

    /// <summary>
    ///     Provides a fallback VoidResult if this result is a failure.
    /// </summary>
    /// <param name="fallback">Function to produce a fallback result from the error</param>
    /// <returns>This result if successful, or the fallback result if failed</returns>
    /// <remarks>
    ///     Use when you want to recover from specific errors with an alternative operation.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VoidResult<TError> OrElse(Func<TError, VoidResult<TError>> fallback)
    {
        ArgumentNullException.ThrowIfNull(fallback);

        return IsSuccess ? this : fallback(_error!);
    }

    // =============================================================================
    // Operations (Map, Then)
    // =============================================================================

    /// <summary>
    ///     Maps the error to a new type.
    /// </summary>
    /// <typeparam name="TNewError">The new error type</typeparam>
    /// <param name="mapper">Error mapping function</param>
    /// <returns>VoidResult with mapped error, or Success if original was Success</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VoidResult<TNewError> MapError<TNewError>(Func<TError, TNewError> mapper)
        where TNewError : IError
    {
        ArgumentNullException.ThrowIfNull(mapper);

        return IsSuccess
            ? VoidResult<TNewError>.Success()
            : VoidResult<TNewError>.Failure(mapper(_error!));
    }

    /// <summary>
    ///     Maps a void success to a Result with a value.
    ///     If this is a failure, propagates the error.
    /// </summary>
    /// <typeparam name="T">The value type for the resulting Result.</typeparam>
    /// <param name="valueFactory">Factory for the success value.</param>
    /// <returns>Result with the value, or failure with the original error.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<T, TError> Map<T>(Func<T> valueFactory)
    {
        ArgumentNullException.ThrowIfNull(valueFactory);

        return IsSuccess
            ? Result<T, TError>.Success(valueFactory())
            : Result<T, TError>.Failure(_error!);
    }

    /// <summary>
    ///     Chains another void operation if this one succeeded.
    /// </summary>
    /// <param name="next">The next operation to execute</param>
    /// <returns>The result of the next operation, or the current failure</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VoidResult<TError> Then(Func<VoidResult<TError>> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return IsSuccess ? next() : this;
    }

    // =============================================================================
    // Implicit Conversions
    // =============================================================================

    /// <summary>
    ///     Implicit conversion from error to VoidResult.Failure.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator VoidResult<TError>(TError error)
    {
        return Failure(error);
    }

    /// <summary>
    ///     Implicit conversion to bool (true if success, false if failure).
    ///     Allows: if (result) { ... }
    /// </summary>
    /// <remarks>
    ///     This returns the success state, so <c>if (result)</c> compiles and behaves as a
    ///     success check. The conversion is intentional and is kept for ergonomics.
    ///     <para>
    ///     Be aware that it can also hide intent and accidentally consume a result in a boolean
    ///     context — for example a result may be implicitly coerced to <see langword="bool"/> in a
    ///     conditional, logical (<c>&amp;&amp;</c> / <c>||</c>) or ternary expression where a
    ///     success/failure decision was never explicitly intended, silently discarding the error.
    ///     </para>
    ///     <para>
    ///     Prefer explicitly checking <see cref="IsSuccess"/> (or <see cref="IsFailure"/>) or using
    ///     <see cref="Match{TResult}"/> so the success and failure paths are both visible at the call site.
    ///     </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator bool(VoidResult<TError> result)
    {
        return result.IsSuccess;
    }

    // =============================================================================
    // Formatting
    // =============================================================================

    /// <summary>
    ///     Returns a human-readable representation of this result.
    /// </summary>
    public override string ToString() => IsSuccess ? "Success" : $"Failure({_error})";

    // =============================================================================
    // Equality
    // =============================================================================

    /// <inheritdoc />
    public bool Equals(VoidResult<TError> other)
    {
        if (IsSuccess != other.IsSuccess)
            return false;

        return IsSuccess || EqualityComparer<TError>.Default.Equals(_error, other._error);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is VoidResult<TError> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return IsSuccess
            ? HashCode.Combine(true)
            : HashCode.Combine(false, EqualityComparer<TError>.Default.GetHashCode(_error!));
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(VoidResult<TError> left, VoidResult<TError> right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(VoidResult<TError> left, VoidResult<TError> right) => !left.Equals(right);

    // =============================================================================
    // Exception Helpers (keep out of hot path)
    // =============================================================================

    [DoesNotReturn]
    private void ThrowErrorAccessInvalid()
    {
        // Distinguish the two invalid states for a clear, actionable message.
        if (IsSuccess)
            throw new InvalidOperationException(
                "Cannot access Error when operation succeeded. " +
                "Check IsFailure before accessing Error, use TryGetError(), or use Match().");

        throw new InvalidOperationException(
            "Cannot access Error on an uninitialized VoidResult. " +
            "A VoidResult was not created via Success() or Failure(...) " +
            "(it is a default/zero-initialized struct, which is not a valid value).");
    }
}