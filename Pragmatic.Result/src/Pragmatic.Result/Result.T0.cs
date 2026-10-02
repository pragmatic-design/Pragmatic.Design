using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Pragmatic.Result;

/// <summary>
///     Represents the result of an operation that can either succeed with a value
///     or fail with an untyped error (Error base class).
/// </summary>
/// <typeparam name="TValue">The type of the success value.</typeparam>
/// <remarks>
///     <para>
///         Use this variant when you don't want to specify the exact error types
///         at compile time. For type-safe error handling, prefer
///         <see cref="Result{TValue, TError}" /> or multi-error variants.
///     </para>
///     <para>
///         Zero-allocation struct with sub-nanosecond performance.
///     </para>
///     <para>
///         <b>Invariant — <c>default(Result&lt;TValue&gt;)</c> is NOT a valid value.</b>
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
public readonly struct Result<TValue> : IResultBase, IEquatable<Result<TValue>>
{
    private readonly TValue? _value;
    private readonly Error? _error;

    private Result(TValue? value, Error? error, bool isSuccess)
    {
        _value = value;
        _error = error;
        IsSuccess = isSuccess;
    }

    // =============================================================================
    // State Properties
    // =============================================================================

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

    // =============================================================================
    // Unsafe Access (throwing)
    // =============================================================================

    /// <summary>
    ///     Gets the success value.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if IsSuccess is false.</exception>
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
    public Error Error
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
    // IResultBase Implementation
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue([MaybeNullWhen(false)] out TValue value)
    {
        value = _value;
        return IsSuccess;
    }

    /// <summary>
    ///     Attempts to get the error without throwing.
    /// </summary>
    /// <remarks>
    ///     An uninitialized <c>default(Result)</c> returns false (it does NOT hand out a null error).
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetError([MaybeNullWhen(false)] out Error error)
    {
        // Gate on the error itself, not on IsSuccess: a default(Result) has IsSuccess == false but a null
        // error, and must never be surfaced as a failure with a null error.
        error = _error;
        return _error is not null;
    }

    // =============================================================================
    // Factory Methods
    // =============================================================================

    /// <summary>
    ///     Creates a successful result with a value.
    /// </summary>
    /// <remarks>
    ///     <c>null</c> is accepted on purpose: <c>TValue</c> may legitimately be a
    ///     nullable value type (e.g. <c>Result&lt;int?&gt;</c>) or a nullable reference
    ///     type where <c>null</c> encodes "success, no value". Callers that want to
    ///     reject <c>null</c> should validate at their own call site.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TValue> Success(TValue value)
    {
        return new Result<TValue>(value, default, true);
    }

    /// <summary>
    ///     Creates a failed result with an error.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Result<TValue> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new Result<TValue>(default, error, false);
    }

    // =============================================================================
    // Pattern Matching
    // =============================================================================

    /// <summary>
    ///     Matches the result and executes the corresponding function.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TResult Match<TResult>(
        Func<TValue, TResult> onSuccess,
        Func<Error, TResult> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess ? onSuccess(_value!) : onFailure(_error!);
    }

    /// <summary>
    ///     Matches the result and executes the corresponding action.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Match(
        Action<TValue> onSuccess,
        Action<Error> onFailure)
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
        Func<Error, Task<TResult>> onFailure)
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
        Func<Error, Task> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess
            ? onSuccess(_value!)
            : onFailure(_error!);
    }

    // =============================================================================
    // LINQ Operations
    // =============================================================================

    /// <summary>
    ///     Maps the success value to a new type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<TNew> Map<TNew>(Func<TValue, TNew> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);

        return IsSuccess
            ? Result<TNew>.Success(mapper(_value!))
            : Result<TNew>.Failure(_error!);
    }

    /// <summary>
    ///     Chains another operation that returns a Result.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Result<TNew> Bind<TNew>(Func<TValue, Result<TNew>> binder)
    {
        ArgumentNullException.ThrowIfNull(binder);

        return IsSuccess ? binder(_value!) : Result<TNew>.Failure(_error!);
    }

    // =============================================================================
    // Implicit Conversions
    // =============================================================================

    /// <summary>
    ///     Implicitly converts a value to a successful result.
    /// </summary>
    /// <remarks>
    ///     HAZARD: this conversion is deliberately lenient and does NOT validate the value. Passing a
    ///     <c>null</c> reference produces a SUCCESS result wrapping <c>null</c> — there is no null check,
    ///     so a null can silently become a "successful" result. This leniency is intentional. If you want
    ///     null rejected, prefer <c>Result&lt;TValue&gt;.Success(value)</c> (which validates), or ensure the
    ///     value is non-null before relying on the implicit conversion.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Result<TValue>(TValue value)
    {
        return Success(value);
    }

    /// <summary>
    ///     Implicitly converts a result to its value.
    /// </summary>
    /// <remarks>
    ///     This conversion THROWS <see cref="InvalidOperationException"/> on a failure result — by
    ///     deliberate analogy with <see cref="System.Nullable{T}"/>'s <c>Value</c>: it makes the happy
    ///     path read cleanly while still failing loudly on misuse. Because it can throw, avoid relying on
    ///     it in contexts where an exception is surprising (LINQ projections, collection initializers).
    ///     For non-throwing access use <c>Match</c>, <c>TryGetValue</c>, or <c>GetValueOrDefault</c>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Thrown if the result is a failure.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator TValue(Result<TValue> result)
    {
        if (!result.IsSuccess)
            ThrowValueAccessOnFailure();
        return result._value!;
    }

    /// <summary>
    ///     Implicitly converts an error to a failed result.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Result<TValue>(Error error)
    {
        return Failure(error);
    }

    // =============================================================================
    // Formatting
    // =============================================================================

    /// <summary>
    ///     Returns a human-readable representation of this result.
    /// </summary>
    public override string ToString() => IsSuccess ? $"Success({_value})" : $"Failure({_error})";

    // =============================================================================
    // Deconstruction
    // =============================================================================

    /// <summary>
    ///     Deconstructs the result for pattern matching.
    /// </summary>
    public void Deconstruct(out bool isSuccess, out TValue? value, out Error? error)
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
    // Equality
    // =============================================================================

    /// <inheritdoc />
    public bool Equals(Result<TValue> other)
    {
        if (IsSuccess != other.IsSuccess)
            return false;

        return IsSuccess
            ? EqualityComparer<TValue>.Default.Equals(_value, other._value)
            : EqualityComparer<Error>.Default.Equals(_error, other._error);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Result<TValue> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return IsSuccess
            ? HashCode.Combine(true, _value is null ? 0 : EqualityComparer<TValue>.Default.GetHashCode(_value))
            : HashCode.Combine(false, _error is null ? 0 : _error.GetHashCode());
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(Result<TValue> left, Result<TValue> right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(Result<TValue> left, Result<TValue> right) => !left.Equals(right);

    // =============================================================================
    // Exception Helpers
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