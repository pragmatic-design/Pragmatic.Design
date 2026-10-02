// =============================================================================
// Pragmatic.Patch - Optional<T>
// Tri-state wrapper: undefined (not present) vs null vs value
// =============================================================================

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Pragmatic.Patch;

/// <summary>
///     Tri-state wrapper that distinguishes between "not sent" (undefined),
///     explicitly null, and a concrete value. Essential for PATCH semantics
///     where omitting a field means "don't change" vs sending null means "clear it".
/// </summary>
/// <typeparam name="T">The wrapped value type.</typeparam>
public readonly struct Optional<T> : IEquatable<Optional<T>>
{
    private readonly T? _value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Optional(T? value, bool hasValue)
    {
        _value = value;
        HasValue = hasValue;
    }

    /// <summary>
    ///     Whether a value was explicitly provided (including null).
    ///     False means the field was not present in the input at all.
    /// </summary>
    public bool HasValue
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get;
    }

    /// <summary>
    ///     Whether this represents an undefined (not-sent) field.
    /// </summary>
    public bool IsUndefined
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => !HasValue;
    }

    /// <summary>
    ///     The value, if present. Throws if undefined.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown when accessing value of an undefined Optional.</exception>
    public T? Value
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => HasValue
            ? _value
            : throw new InvalidOperationException("Cannot access value of an undefined Optional<T>. Check HasValue first.");
    }

    /// <summary>
    ///     Returns an undefined Optional (field not present in input).
    /// </summary>
    public static Optional<T> Undefined => default;

    /// <summary>
    ///     Returns an Optional representing an explicit null value.
    /// </summary>
    public static Optional<T> Null => new(default, true);

    /// <summary>
    ///     Creates an Optional with a concrete value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Optional<T> Of(T? value) => new(value, true);

    /// <summary>
    ///     Implicit conversion from a value to Optional.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator Optional<T>(T? value) => new(value, true);

    /// <summary>
    ///     Gets the value if present, or the specified default.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    [return: NotNullIfNotNull(nameof(defaultValue))]
    public T? GetValueOrDefault(T? defaultValue = default) => HasValue ? _value : defaultValue;

    /// <summary>
    ///     Applies the value to an action if present.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void IfPresent(Action<T?> action)
    {
        if (HasValue)
            action(_value);
    }

    /// <summary>
    ///     Projects the value through <paramref name="selector"/> if present; returns <c>default</c> otherwise.
    /// </summary>
    /// <typeparam name="TResult">The return type of the projection.</typeparam>
    /// <param name="selector">The projection to apply when a value is present.</param>
    /// <returns>The projected result, or <c>default(TResult)</c> when <see cref="IsUndefined"/> is <c>true</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TResult? IfPresent<TResult>(Func<T?, TResult> selector)
        => HasValue ? selector(_value) : default;

    /// <summary>
    ///     Maps the value if present, returns undefined otherwise.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Optional<TResult> Map<TResult>(Func<T?, TResult?> mapper)
        => HasValue ? Optional<TResult>.Of(mapper(_value)) : Optional<TResult>.Undefined;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(Optional<T> other)
    {
        if (!HasValue && !other.HasValue)
            return true;
        if (HasValue != other.HasValue)
            return false;
        return EqualityComparer<T>.Default.Equals(_value, other._value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override bool Equals(object? obj) => obj is Optional<T> other && Equals(other);

    /// <inheritdoc />
    /// <remarks>
    ///     Hash sentinel convention:
    ///     <list type="bullet">
    ///         <item><description><b>Undefined</b> → <c>-1</c>. A wrapped value whose own <c>GetHashCode()</c>
    ///         also returns -1 will collide with <c>Undefined</c> in hash-based collections.
    ///         Callers that mix <c>Undefined</c> and arbitrary values in a dictionary or set should use
    ///         a custom <see cref="IEqualityComparer{T}"/> or avoid relying on the sentinel
    ///         (equality via <see cref="Equals(Optional{T})"/> is always collision-free).</description></item>
    ///         <item><description><b>Optional(null)</b> → <c>0</c>.</description></item>
    ///         <item><description><b>Optional(value)</b> → <c>value.GetHashCode()</c>.</description></item>
    ///     </list>
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => HasValue
        ? _value is not null ? _value.GetHashCode() : 0
        : -1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator ==(Optional<T> left, Optional<T> right) => left.Equals(right);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool operator !=(Optional<T> left, Optional<T> right) => !left.Equals(right);

    public override string ToString() => HasValue
        ? _value is not null ? $"Optional({_value})" : "Optional(null)"
        : "Undefined";
}
