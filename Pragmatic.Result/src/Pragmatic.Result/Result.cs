using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Pragmatic.Result;

/// <summary>
///     Represents the result of an operation that can either succeed with a value or fail with an error.
/// </summary>
/// <remarks>
///     <para>
///         This is a zero-allocation discriminated union type optimized for performance.
///         The wrapper itself never allocates; the payload (TValue, TError) may allocate if it's a reference type.
///     </para>
///     <para>
///         <b>Important:</b> Access Value only after checking IsSuccess.
///         Access Error only after checking IsFailure.
///         For safe access without exceptions, use TryGetValue/TryGetError.
///     </para>
///     <para>
///         <b>Invariant — <c>default(Result&lt;TValue, TError&gt;)</c> is NOT a valid value.</b>
///         A Result MUST be created via <see cref="Success"/> or <see cref="Failure"/>. The default
///         (uninitialized) struct has <c>IsSuccess == false</c> and a <c>null</c> error, which is neither
///         a real success nor a real failure. Because every genuine failure carries a non-null error
///         (<see cref="Failure"/> rejects null), the default state is detected as "failure with null error"
///         and is treated as uninitialized: <see cref="TryGetError"/> returns <c>false</c> (it never hands
///         out a null error as if it were a failure), and reading <see cref="Error"/> throws an
///         <see cref="InvalidOperationException"/>. This prevents a default Result from silently
///         propagating as <c>Failure(null)</c>.
///     </para>
/// </remarks>
/// <typeparam name="TValue">The type of the success value</typeparam>
/// <typeparam name="TError">The type of the error, must extend Error</typeparam>
public readonly struct Result<TValue, TError> : IResultBase, IEquatable<Result<TValue, TError>>
    where TError : IError
{
    private readonly TValue? _value;
    private readonly TError? _error;

    private Result(TValue value)
    {
        IsSuccess = true;
        _value = value;
        _error = default;
    }

    private Result(TError error)
    {
        IsSuccess = false;
        _value = default;
        _error = error;
    }

    /// <summary>
    ///     Gets whether this result represents a successful operation.
    /// </summary>
    public bool IsSuccess
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    }

    /// <summary>
    ///     Gets whether this result represents a failed operation.
    /// </summary>
    public bool IsFailure
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => !IsSuccess;
    }

    /// <summary>
    ///     Gets the success value.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if IsFailure is true.</exception>
    /// <remarks>
    ///     Use only when IsSuccess is true.
    ///     For safer access without exceptions, use TryGetValue() or Match().
    /// </remarks>
    public TValue Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (!IsSuccess)
                ThrowValueAccessOnFailure();
            return _value!;
        }
    }

    /// <summary>
    ///     Gets the error.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if IsSuccess is true, or if the Result is uninitialized (a <c>default</c> struct that was
    ///     not created via <see cref="Success"/>/<see cref="Failure"/>, and therefore carries a null error).
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
            // means either success or an uninitialized default(Result) — both are invalid Error access.
            if (_error is null)
                ThrowErrorAccessInvalid();
            return _error;
        }
    }

    // =============================================================================
    // IResultBase Implementation (for runtime identification)
    // =============================================================================

    /// <inheritdoc />
    bool IResultBase.HasValueType => true; // Result<T> has a value type

    /// <inheritdoc />
    object? IResultBase.ValueAsObject => IsSuccess ? _value : null;

    /// <inheritdoc />
    IError? IResultBase.ErrorAsObject => IsSuccess ? null : _error;

    // =============================================================================
    // Safe Access (non-throwing)
    // =============================================================================

    /// <summary>
    ///     Attempts to get the success value without throwing.
    /// </summary>
    /// <param name="value">The success value if IsSuccess is true; otherwise, default.</param>
    /// <returns>True if the result is successful; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue([MaybeNullWhen(false)] out TValue value)
    {
        value = _value;
        return IsSuccess;
    }

    /// <summary>
    ///     Attempts to get the error without throwing.
    /// </summary>
    /// <param name="error">The error if this is a genuine failure; otherwise, default.</param>
    /// <returns>
    ///     True if the result is a genuine failure carrying a non-null error; otherwise, false.
    ///     An uninitialized <c>default(Result)</c> returns false (it does NOT hand out a null error).
    /// </returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetError([MaybeNullWhen(false)] out TError error)
    {
        // Gate on the error itself, not on IsSuccess: a default(Result) has IsSuccess == false but a null
        // error, and must never be surfaced as a failure with a null error (which callers would propagate
        // as Failure(null)).
        error = _error;
        return _error is not null;
    }

    // =============================================================================
    // Factory Methods
    // =============================================================================

    /// <summary>
    ///     Creates a successful result with the given value.
    /// </summary>
    /// <param name="value">The success value (cannot be null)</param>
    /// <returns>A successful result</returns>
    /// <exception cref="ArgumentNullException">Thrown if value is null.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TValue, TError> Success(TValue value)
    {
        // `value is null` instead of ArgumentNullException.ThrowIfNull(value): for an unconstrained
        // generic, ThrowIfNull takes `object?` and BOXES a value-type TValue on every call (a needless
        // hot-path allocation, since a non-nullable value type can never be null). The pattern-match
        // form is compiled away by the JIT for non-nullable value types (zero cost) and still rejects
        // a genuine null for reference types and Nullable<T>.
        if (value is null)
            throw new ArgumentNullException(nameof(value));
        return new Result<TValue, TError>(value);
    }

    /// <summary>
    ///     Creates a failed result with the given error.
    /// </summary>
    /// <param name="error">The error (cannot be null)</param>
    /// <returns>A failed result</returns>
    /// <exception cref="ArgumentNullException">Thrown if error is null.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TValue, TError> Failure(TError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<TValue, TError>(error);
    }

    // =============================================================================
    // Pattern Matching
    // =============================================================================

    /// <summary>
    ///     Pattern matches on the result, executing the appropriate function based on success or failure.
    /// </summary>
    /// <typeparam name="TResult">The return type</typeparam>
    /// <param name="onSuccess">Function to execute if successful</param>
    /// <param name="onFailure">Function to execute if failed</param>
    /// <returns>The result of the executed function</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TResult Match<TResult>(
        Func<TValue, TResult> onSuccess,
        Func<TError, TResult> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess
            ? onSuccess(_value!)
            : onFailure(_error!);
    }

    /// <summary>
    ///     Pattern matches on the result, executing the appropriate action based on success or failure.
    /// </summary>
    /// <param name="onSuccess">Action to execute if successful</param>
    /// <param name="onFailure">Action to execute if failed</param>
    public void Match(
        Action<TValue> onSuccess,
        Action<TError> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        if (IsSuccess)
            onSuccess(_value!);
        else
            onFailure(_error!);
    }

    // =============================================================================
    // Async Match
    // =============================================================================

    /// <summary>
    ///     Asynchronously matches on success or failure, returning the result of the matching function.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<TResult> MatchAsync<TResult>(
        Func<TValue, Task<TResult>> onSuccess,
        Func<TError, Task<TResult>> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess
            ? onSuccess(_value!)
            : onFailure(_error!);
    }

    /// <summary>
    ///     Asynchronously matches on success or failure, executing the matching action.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task MatchAsync(
        Func<TValue, Task> onSuccess,
        Func<TError, Task> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess
            ? onSuccess(_value!)
            : onFailure(_error!);
    }

    // =============================================================================
    // LINQ Operations (Map, Bind, MapError)
    // =============================================================================

    /// <summary>
    ///     Maps the success value to a new type.
    /// </summary>
    /// <typeparam name="TNewValue">The new value type</typeparam>
    /// <param name="mapper">Function to map the value</param>
    /// <returns>A new result with the mapped value, or the original error</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<TNewValue, TError> Map<TNewValue>(Func<TValue, TNewValue> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);

        return IsSuccess
            ? Result<TNewValue, TError>.Success(mapper(_value!))
            : Result<TNewValue, TError>.Failure(_error!);
    }

    /// <summary>
    ///     Binds the result to a new result-returning function (flatMap/chain).
    /// </summary>
    /// <typeparam name="TNewValue">The new value type</typeparam>
    /// <param name="binder">Function that returns a new result</param>
    /// <returns>The result of the binder function or the original error</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<TNewValue, TError> Bind<TNewValue>(
        Func<TValue, Result<TNewValue, TError>> binder)
    {
        ArgumentNullException.ThrowIfNull(binder);

        return IsSuccess
            ? binder(_value!)
            : Result<TNewValue, TError>.Failure(_error!);
    }

    /// <summary>
    ///     Maps the error to a new type.
    /// </summary>
    /// <typeparam name="TNewError">The new error type</typeparam>
    /// <param name="mapper">Function to map the error</param>
    /// <returns>A new result with the mapped error, or the original value</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<TValue, TNewError> MapError<TNewError>(Func<TError, TNewError> mapper)
        where TNewError : IError
    {
        ArgumentNullException.ThrowIfNull(mapper);

        return IsSuccess
            ? Result<TValue, TNewError>.Success(_value!)
            : Result<TValue, TNewError>.Failure(mapper(_error!));
    }

    // =============================================================================
    // Implicit Conversions
    // =============================================================================

    /// <summary>
    ///     Implicitly converts a value to a successful result.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Result<TValue, TError>(TValue value)
    {
        return Success(value);
    }

    /// <summary>
    ///     Implicitly converts an error to a failed result.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Result<TValue, TError>(TError error)
    {
        return Failure(error);
    }

    /// <summary>
    ///     Implicitly converts a result to its value.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         This allows using Result as if it were T directly, after checking for failure:
    ///     </para>
    ///     <code>
    ///     var result = GetUser(id);
    ///     if (result.IsFailure) return result.Error;
    ///     User user = result; // implicit conversion
    ///     </code>
    ///     <para>
    ///         This conversion THROWS <see cref="InvalidOperationException"/> when invoked on a failure
    ///         result. This is a deliberate <see cref="System.Nullable{T}.Value"/>-style ergonomic: the
    ///         assignment <c>User user = result;</c> reads as safe but can throw at runtime, so the throw
    ///         is intentional rather than a defect.
    ///     </para>
    ///     <para>
    ///         Because it can throw, AVOID relying on this operator inside contexts where the failure path
    ///         is not obvious or cannot be guarded — in particular LINQ projections (e.g. <c>Select</c>)
    ///         and collection initializers, where the implicit conversion may fire on a failure result and
    ///         surface as an unexpected exception.
    ///     </para>
    ///     <para>
    ///         For non-throwing access prefer <see cref="Match{TResult}"/>, <c>TryGetValue</c>, or
    ///         <c>GetValueOrDefault</c>.
    ///     </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown if the result is a failure.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator TValue(Result<TValue, TError> result)
    {
        if (!result.IsSuccess)
            ThrowValueAccessOnFailure();
        return result._value!;
    }

    // =============================================================================
    // Deconstruction
    // =============================================================================

    /// <summary>
    ///     Deconstructs the result for pattern matching.
    /// </summary>
    /// <example>
    ///     var (isSuccess, value, error) = result;
    ///     if (isSuccess) Console.WriteLine(value);
    /// </example>
    public void Deconstruct(out bool isSuccess, out TValue? value, out TError? error)
    {
        isSuccess = IsSuccess;
        value = _value;
        error = _error;
    }

    /// <summary>
    ///     Deconstructs the result for success-focused pattern matching.
    /// </summary>
    /// <example>
    ///     var (isSuccess, value) = result;
    ///     if (isSuccess) Console.WriteLine(value);
    /// </example>
    public void Deconstruct(out bool isSuccess, out TValue? value)
    {
        isSuccess = IsSuccess;
        value = _value;
    }

    // =============================================================================
    // Formatting
    // =============================================================================

    /// <summary>
    ///     Returns a human-readable representation of this result.
    /// </summary>
    public override string ToString() => IsSuccess ? $"Success({_value})" : $"Failure({_error})";

    // =============================================================================
    // Equality
    // =============================================================================

    /// <inheritdoc />
    public bool Equals(Result<TValue, TError> other)
    {
        if (IsSuccess != other.IsSuccess)
            return false;

        return IsSuccess
            ? EqualityComparer<TValue>.Default.Equals(_value, other._value)
            : EqualityComparer<TError>.Default.Equals(_error, other._error);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Result<TValue, TError> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        // Guard the null payload of a default(Result<,>): EqualityComparer.GetHashCode(null!) would NRE
        // for a value-type argument and the `!` is logically wrong for the default (unset) state.
        return IsSuccess
            ? HashCode.Combine(true, _value is null ? 0 : EqualityComparer<TValue>.Default.GetHashCode(_value))
            : HashCode.Combine(false, _error is null ? 0 : EqualityComparer<TError>.Default.GetHashCode(_error));
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(Result<TValue, TError> left, Result<TValue, TError> right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(Result<TValue, TError> left, Result<TValue, TError> right) => !left.Equals(right);

    // =============================================================================
    // Exception Helpers (keep out of hot path)
    // =============================================================================

    [DoesNotReturn]
    private static void ThrowValueAccessOnFailure()
    {
        throw new InvalidOperationException(
            "Cannot access Value when result is failure. " +
            "Check IsSuccess before accessing Value, use TryGetValue(), or use Match().");
    }

    [DoesNotReturn]
    private void ThrowErrorAccessInvalid()
    {
        // Distinguish the two invalid states for a clear, actionable message.
        if (IsSuccess)
            throw new InvalidOperationException(
                "Cannot access Error when result is success. " +
                "Check IsFailure before accessing Error, use TryGetError(), or use Match().");

        throw new InvalidOperationException(
            "Cannot access Error on an uninitialized Result. " +
            "A Result was not created via Success(...) or Failure(...) " +
            "(it is a default/zero-initialized struct, which is not a valid value).");
    }
}