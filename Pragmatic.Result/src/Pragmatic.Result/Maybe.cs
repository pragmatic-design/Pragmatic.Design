using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Pragmatic.Result;

/// <summary>
///     Represents an optional value (Maybe/Option pattern).
///     Can be Some(value) or None.
/// </summary>
/// <remarks>
///     <para>
///         Zero-allocation struct with sub-nanosecond performance.
///         Named "Maybe" to avoid confusion with the Options pattern commonly used in .NET configuration.
///     </para>
///     <para>
///         <b>When to use Maybe vs Result:</b>
///     </para>
///     <para>
///         <b>Maybe&lt;T&gt;</b> - Absence is NOT exceptional:
///         <list type="bullet">
///             <item>Cache lookup (may not be cached)</item>
///             <item>Optional configuration values</item>
///             <item>Parsing soft (TryParse semantics)</item>
///             <item>Collection.FirstOrDefault semantics</item>
///             <item>Dictionary lookup (key may not exist)</item>
///         </list>
///     </para>
///     <para>
///         <b>Result&lt;T, NotFoundError&gt;</b> - Absence is SEMANTIC:
///         <list type="bullet">
///             <item>Domain entity lookup (entity should exist)</item>
///             <item>API resource (404 is a meaningful error)</item>
///             <item>Business invariants (must exist for operation)</item>
///             <item>External service call (failure needs context)</item>
///         </list>
///     </para>
///     <para>
///         <b>Rule of thumb:</b> If the absence is an ERROR, use Result.
///         If the absence is NORMAL, use Maybe.
///     </para>
/// </remarks>
/// <typeparam name="T">The type of the optional value</typeparam>
public readonly struct Maybe<T> : IEquatable<Maybe<T>>
{
    private readonly T? _value;

    private Maybe(T value, bool hasValue)
    {
        _value = value;
        HasValue = hasValue;
    }

    /// <summary>
    ///     Gets whether this option contains a value.
    /// </summary>
    public bool HasValue
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    }

    /// <summary>
    ///     Gets whether this option is empty (None).
    /// </summary>
    public bool IsNone
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => !HasValue;
    }

    /// <summary>
    ///     Gets the value.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if IsNone is true.</exception>
    /// <remarks>
    ///     Use only when HasValue is true.
    ///     For safer access without exceptions, use TryGetValue() or Match().
    /// </remarks>
    public T Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (!HasValue)
                ThrowValueAccessOnNone();
            return _value!;
        }
    }

    // =============================================================================
    // Safe Access (non-throwing)
    // =============================================================================

    /// <summary>
    ///     Attempts to get the value without throwing.
    /// </summary>
    /// <param name="value">The value if HasValue is true; otherwise, default.</param>
    /// <returns>True if the option has a value; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue([MaybeNullWhen(false)] out T value)
    {
        value = _value;
        return HasValue;
    }

    // =============================================================================
    // Factory Methods
    // =============================================================================

    /// <summary>
    ///     Creates an option with a value (Some).
    /// </summary>
    /// <param name="value">The value (cannot be null)</param>
    /// <returns>An option containing the value</returns>
    /// <exception cref="ArgumentNullException">Thrown if value is null.</exception>
    /// <example>
    ///     <code>
    /// Maybe&lt;User&gt; Lookup(int id) =>
    ///     _cache.TryGet(id, out var user) ? Maybe&lt;User&gt;.Some(user) : Maybe&lt;User&gt;.None();
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Maybe<T> Some(T value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new Maybe<T>(value, true);
    }

    /// <summary>
    ///     Creates an empty option (None).
    /// </summary>
    /// <returns>An empty option</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Maybe<T> None()
    {
        return new Maybe<T>(default!, false);
    }

    // =============================================================================
    // Pattern Matching
    // =============================================================================

    /// <summary>
    ///     Matches the option state and executes the corresponding function.
    /// </summary>
    /// <typeparam name="TResult">The result type</typeparam>
    /// <param name="onSome">Function to execute if option has a value</param>
    /// <param name="onNone">Function to execute if option is None</param>
    /// <returns>The result of the executed function</returns>
    /// <example>
    ///     <code>
    /// Maybe&lt;User&gt; user = Lookup(id);
    /// string label = user.Match(
    ///     onSome: u => u.Name,
    ///     onNone: () => "(unknown)");
    /// </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TResult Match<TResult>(
        Func<T, TResult> onSome,
        Func<TResult> onNone)
    {
        ArgumentNullException.ThrowIfNull(onSome);
        ArgumentNullException.ThrowIfNull(onNone);

        return HasValue ? onSome(_value!) : onNone();
    }

    /// <summary>
    ///     Executes an action based on the option state.
    /// </summary>
    /// <param name="onSome">Action to execute if option has a value</param>
    /// <param name="onNone">Action to execute if option is None</param>
    public void Match(
        Action<T> onSome,
        Action onNone)
    {
        ArgumentNullException.ThrowIfNull(onSome);
        ArgumentNullException.ThrowIfNull(onNone);

        if (HasValue)
            onSome(_value!);
        else
            onNone();
    }

    // =============================================================================
    // LINQ Operations (Map, Bind)
    // =============================================================================

    /// <summary>
    ///     Maps the value to a new type if present.
    /// </summary>
    /// <typeparam name="TResult">The new type</typeparam>
    /// <param name="mapper">Mapping function</param>
    /// <returns>Option with mapped value, or None if original was None</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Maybe<TResult> Map<TResult>(Func<T, TResult> mapper)
    {
        ArgumentNullException.ThrowIfNull(mapper);

        return HasValue
            ? Maybe<TResult>.Some(mapper(_value!))
            : Maybe<TResult>.None();
    }

    /// <summary>
    ///     Binds the option to a new option-returning function (flatMap/chain).
    /// </summary>
    /// <typeparam name="TResult">The new type</typeparam>
    /// <param name="binder">Function that returns a new option</param>
    /// <returns>The result of the binder function or None if original was None</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Maybe<TResult> Bind<TResult>(Func<T, Maybe<TResult>> binder)
    {
        ArgumentNullException.ThrowIfNull(binder);

        return HasValue
            ? binder(_value!)
            : Maybe<TResult>.None();
    }

    // =============================================================================
    // Default Value Accessors
    // =============================================================================

    /// <summary>
    ///     Returns the value if present, otherwise returns the provided default.
    /// </summary>
    /// <param name="defaultValue">Default value to return if None</param>
    /// <returns>The value or default</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetValueOrDefault(T defaultValue)
    {
        return HasValue ? _value! : defaultValue;
    }

    /// <summary>
    ///     Returns the value if present, otherwise computes and returns a default.
    /// </summary>
    /// <param name="defaultFactory">Factory to compute default value</param>
    /// <returns>The value or computed default</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public T GetValueOrDefault(Func<T> defaultFactory)
    {
        ArgumentNullException.ThrowIfNull(defaultFactory);
        return HasValue ? _value! : defaultFactory();
    }

    // =============================================================================
    // Implicit Conversions
    // =============================================================================

    /// <summary>
    ///     Implicit conversion from value to Maybe. Converts null to None.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Maybe<T>(T? value)
    {
        return value is null ? None() : Some(value);
    }

    /// <summary>
    ///     Implicit conversion to bool (true if has value, false if None).
    ///     Allows: if (option) { ... }
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator bool(Maybe<T> option)
    {
        return option.HasValue;
    }

    // =============================================================================
    // Equality
    // =============================================================================

    /// <inheritdoc />
    public bool Equals(Maybe<T> other)
    {
        if (HasValue != other.HasValue)
            return false;

        return !HasValue || EqualityComparer<T>.Default.Equals(_value, other._value);
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Maybe<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HasValue
            ? HashCode.Combine(true, EqualityComparer<T>.Default.GetHashCode(_value!))
            : HashCode.Combine(false);
    }

    /// <summary>Equality operator.</summary>
    public static bool operator ==(Maybe<T> left, Maybe<T> right) => left.Equals(right);

    /// <summary>Inequality operator.</summary>
    public static bool operator !=(Maybe<T> left, Maybe<T> right) => !left.Equals(right);

    // =============================================================================
    // Exception Helpers (keep out of hot path)
    // =============================================================================

    [DoesNotReturn]
    private static void ThrowValueAccessOnNone()
    {
        throw new InvalidOperationException(
            "Cannot access Value when option is None. " +
            "Check HasValue before accessing Value, use TryGetValue(), or use Match().");
    }
}