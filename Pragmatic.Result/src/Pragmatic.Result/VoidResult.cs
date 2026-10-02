using System.Runtime.CompilerServices;

namespace Pragmatic.Result;

/// <summary>
///     Represents the result of a void operation that can either succeed or fail.
/// </summary>
/// <remarks>
///     <para>
///         Use when an operation returns no value and has no typed errors.
///         Zero-allocation struct with sub-nanosecond performance.
///     </para>
///     <para>
///         <b>Why VoidResult instead of UnitResult:</b>
///         "Void" is familiar to C# developers, avoids introducing artificial Unit type,
///         and has clear semantics: "operation completed, no value returned".
///     </para>
/// </remarks>
public readonly struct VoidResult : IResultBase, IEquatable<VoidResult>
{
    private VoidResult(bool isSuccess)
    {
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

    // =============================================================================
    // IResultBase Implementation (for runtime identification)
    // =============================================================================

    /// <inheritdoc />
    bool IResultBase.HasValueType => false; // VoidResult carries no value

    /// <inheritdoc />
    object? IResultBase.ValueAsObject => null; // VoidResult has no value

    /// <inheritdoc />
    IError? IResultBase.ErrorAsObject => null; // untyped VoidResult carries no error object

    /// <summary>
    ///     Creates a successful void result.
    /// </summary>
    /// <returns>A successful void result</returns>
    /// <example>
    ///     <code>
    /// public VoidResult DeleteTempFiles()
    /// {
    ///     _cache.Clear();
    ///     return VoidResult.Success();
    /// }
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult Success()
    {
        return new VoidResult(true);
    }

    /// <summary>
    ///     Creates a failed void result.
    /// </summary>
    /// <returns>A failed void result</returns>
    /// <example>
    ///     <code>
    /// public VoidResult TryAcquireLock()
    /// {
    ///     return _lock.TryEnter() ? VoidResult.Success() : VoidResult.Failure();
    /// }
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static VoidResult Failure()
    {
        return new VoidResult(false);
    }

    /// <summary>
    ///     Matches the result state and executes the corresponding function.
    /// </summary>
    /// <typeparam name="TResult">The result type</typeparam>
    /// <param name="onSuccess">Function to execute if operation succeeded</param>
    /// <param name="onFailure">Function to execute if operation failed</param>
    /// <returns>The result of the executed function</returns>
    /// <example>
    ///     <code>
    /// VoidResult result = TryAcquireLock();
    /// string message = result.Match(
    ///     onSuccess: () => "Lock acquired",
    ///     onFailure: () => "Lock busy");
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TResult Match<TResult>(
        Func<TResult> onSuccess,
        Func<TResult> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess ? onSuccess() : onFailure();
    }

    /// <summary>
    ///     Executes an action based on the result state.
    /// </summary>
    /// <param name="onSuccess">Action to execute if operation succeeded</param>
    /// <param name="onFailure">Action to execute if operation failed</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Match(
        Action onSuccess,
        Action onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        if (IsSuccess)
            onSuccess();
        else
            onFailure();
    }

    /// <summary>
    ///     Asynchronously matches on success or failure, returning the result of the matching function.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task<TResult> MatchAsync<TResult>(
        Func<Task<TResult>> onSuccess,
        Func<Task<TResult>> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess
            ? onSuccess()
            : onFailure();
    }

    /// <summary>
    ///     Asynchronously matches on success or failure, executing the matching action.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Task MatchAsync(
        Func<Task> onSuccess,
        Func<Task> onFailure)
    {
        ArgumentNullException.ThrowIfNull(onSuccess);
        ArgumentNullException.ThrowIfNull(onFailure);

        return IsSuccess
            ? onSuccess()
            : onFailure();
    }

    /// <summary>
    ///     Chains another void operation if this one succeeded.
    /// </summary>
    /// <param name="next">The next operation to execute</param>
    /// <returns>The result of the next operation, or the current failure</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public VoidResult Then(Func<VoidResult> next)
    {
        ArgumentNullException.ThrowIfNull(next);
        return IsSuccess ? next() : this;
    }

    /// <summary>
    ///     Implicit conversion to bool (true if success, false if failure).
    ///     Allows: if (result) { ... }
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator bool(VoidResult result)
    {
        return result.IsSuccess;
    }

    /// <summary>
    ///     Explicit conversion from bool to <see cref="VoidResult" />: maps <c>true</c> to a success
    ///     result and <c>false</c> to a failure result.
    /// </summary>
    /// <remarks>
    ///     Provided ONLY as an ergonomic shorthand for terse success/failure returns. In normal code
    ///     prefer the explicit <see cref="Success" /> / <see cref="Failure" /> factories: they make the
    ///     intent clear and let a real <see cref="IError" /> be attached on failure, which a raw
    ///     <c>false</c> cannot carry.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static explicit operator VoidResult(bool isSuccess)
    {
        return isSuccess ? Success() : Failure();
    }

    // =============================================================================
    // Equality
    // =============================================================================

    /// <inheritdoc />
    public bool Equals(VoidResult other) => IsSuccess == other.IsSuccess;

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is VoidResult other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => IsSuccess ? 1 : 0;

    /// <summary>Equality operator.</summary>
    public static bool operator ==(VoidResult left, VoidResult right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(VoidResult left, VoidResult right) => !left.Equals(right);
}