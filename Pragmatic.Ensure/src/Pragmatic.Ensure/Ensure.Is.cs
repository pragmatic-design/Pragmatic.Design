using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Pragmatic.Ensure;

/// <summary>
///     Static guard methods for conditional validation.
///     Methods in this partial return boolean values for use in conditional logic.
/// </summary>
/// <example>
///     <code>
/// using static Pragmatic.Ensure.Ensure;
/// 
/// if (!IsNotNullOrWhiteSpace(name))
///     name = "Unknown";
/// 
/// if (!IsEmail(email))
///     // skip email notifications
/// </code>
/// </example>
public static partial class Ensure
{

    /// <summary>
    ///     Returns true if the Guid is not empty.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNotEmpty(Guid value)
    {
        return value != Guid.Empty;
    }

    /// <summary>
    ///     Returns true if the value is a defined value of the enum type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsDefined<TEnum>(TEnum value) where TEnum : struct, Enum
    {
        return Enum.IsDefined(value);
    }

    /// <summary>
    ///     Returns true if the reference type value is not null.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNotNull<T>([NotNullWhen(true)] T? value) where T : class
    {
        return value is not null;
    }

    /// <summary>
    ///     Returns true if the nullable value type has a value.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNotNull<T>([NotNullWhen(true)] T? value) where T : struct
    {
        return value.HasValue;
    }

    /// <summary>
    ///     Returns true if the string is not null or empty.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNotNullOrEmpty([NotNullWhen(true)] string? value)
    {
        return !string.IsNullOrEmpty(value);
    }

    /// <summary>
    ///     Returns true if the string is not null, empty, or whitespace.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNotNullOrWhiteSpace([NotNullWhen(true)] string? value)
    {
        return !string.IsNullOrWhiteSpace(value);
    }

    /// <summary>
    ///     Returns true if the string length is within the specified range.
    ///     Returns true if value is null (null-safe — null passes the check).
    ///     Because null returns true, <c>[NotNullWhen(true)]</c> cannot be applied;
    ///     callers that need null-narrowing should combine this with a prior null check
    ///     or use <see cref="IsNotNullOrEmpty(string?)" />.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsLengthInRange(string? value, int minLength, int maxLength)
    {
        return value is null || (value.Length >= minLength && value.Length <= maxLength);
    }

    /// <summary>
    ///     Returns true if the string is a valid email address.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsEmail([NotNullWhen(true)] string? value)
    {
        return value is not null && IsEmailInternal(value);
    }

    /// <summary>
    ///     Returns true if the string matches the specified regex pattern.
    ///     Returns false if value is null (consistent with <see cref="IsEmail" /> and <see cref="IsPhone" />).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsMatch([NotNullWhen(true)] string? value, [StringSyntax(StringSyntaxAttribute.Regex)] string pattern)
    {
        return value is not null && IsMatchInternal(value, pattern, throwOnTimeout: false);
    }

    /// <summary>
    ///     Returns true if the string is a valid phone number.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPhone([NotNullWhen(true)] string? value)
    {
        return value is not null && IsPhoneInternal(value, throwOnTimeout: false);
    }

    /// <summary>
    ///     Returns true if the string is a valid URL.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsUrl([NotNullWhen(true)] string? value, bool requireHttps = false)
    {
        return value is not null && IsUrlInternal(value, requireHttps);
    }

    /// <summary>
    ///     Returns true if the string is a valid credit card number (Luhn algorithm, ISO/IEC 7812).
    ///     Spaces and hyphens are ignored; accepts 13-19 digit numbers. Validates the checksum only.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsCreditCard([NotNullWhen(true)] string? value)
    {
        return value is not null && IsCreditCardInternal(value);
    }

    /// <summary>
    ///     Returns true if the string does not contain the specified substring.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool DoesNotContain([NotNullWhen(true)] string? value, string substring)
    {
        return value is not null && !value.Contains(substring, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Returns true if the string does not start with the specified prefix.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool DoesNotStartWith([NotNullWhen(true)] string? value, string prefix)
    {
        return value is not null && !value.StartsWith(prefix, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Returns true if the string does not end with the specified suffix.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool DoesNotEndWith([NotNullWhen(true)] string? value, string suffix)
    {
        return value is not null && !value.EndsWith(suffix, StringComparison.Ordinal);
    }

    /// <summary>
    ///     Returns true if the value is positive (greater than zero).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPositive<T>(T value) where T : INumber<T>
    {
        return T.IsPositive(value) && !T.IsZero(value);
    }

    /// <summary>
    ///     Returns true if the value is negative (less than zero).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNegative<T>(T value) where T : INumber<T>
    {
        return T.IsNegative(value);
    }

    /// <summary>
    ///     Returns true if the value is zero.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsZero<T>(T value) where T : INumber<T>
    {
        return T.IsZero(value);
    }

    /// <summary>
    ///     Returns true if the value is not zero.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNotZero<T>(T value) where T : INumber<T>
    {
        return !T.IsZero(value);
    }

    /// <summary>
    ///     Returns true if the value is not negative (zero or positive).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNotNegative<T>(T value) where T : INumber<T>
    {
        return !T.IsNegative(value);
    }

    /// <summary>
    ///     Returns true if the value is within the specified range (inclusive).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsInRange<T>(T value, T min, T max) where T : IComparable<T>
    {
        return value.CompareTo(min) >= 0 && value.CompareTo(max) <= 0;
    }

    /// <summary>
    ///     Returns true if the value is at least the minimum.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAtLeastMin<T>(T value, T minimum) where T : IComparable<T>
    {
        return value.CompareTo(minimum) >= 0;
    }

    /// <summary>
    ///     Returns true if the value is at most the maximum.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsAtMostMax<T>(T value, T maximum) where T : IComparable<T>
    {
        return value.CompareTo(maximum) <= 0;
    }

    /// <summary>
    ///     Returns true if the collection contains no duplicate elements.
    ///     Returns true if value is null (null-safe).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool HasNoDuplicates<T>([NotNullWhen(true)] IEnumerable<T>? value)
    {
        if (value is null)
            return true;

        var set = new HashSet<T>();
        foreach (var item in value)
            if (!set.Add(item))
                return false;

        return true;
    }

    /// <summary>
    ///     Returns true if the collection is not null and not empty.
    /// </summary>
    /// <remarks>
    ///     A single <see cref="IEnumerable{T}" /> overload serves every collection type via
    ///     <see cref="System.Linq.Enumerable.TryGetNonEnumeratedCount{TSource}(IEnumerable{TSource}, out int)" />
    ///     (O(1) for <see cref="ICollection{T}" />, <see cref="IReadOnlyCollection{T}" />, and arrays), so passing a
    ///     concrete <c>List&lt;T&gt;</c>/<c>HashSet&lt;T&gt;</c> is never ambiguous.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNotNullOrEmpty<T>([NotNullWhen(true)] IEnumerable<T>? value)
    {
        if (value is null)
            return false;
        // O(1) for ICollection/IReadOnlyCollection/array; single MoveNext() for lazy sequences.
        return System.Linq.Enumerable.TryGetNonEnumeratedCount(value, out var count)
            ? count > 0
            : value.Any();
    }

    /// <summary>
    ///     Returns true if the array is not null and not empty.
    ///     Optimized overload for arrays (more specific than the <see cref="IEnumerable{T}" /> overload).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNotNullOrEmpty<T>([NotNullWhen(true)] T[]? value)
    {
        return value is not null && value.Length > 0;
    }

    /// <summary>
    ///     Returns true if the DateTime is in the past.
    /// </summary>
    /// <remarks>
    ///     If <paramref name="value"/> has <see cref="DateTimeKind.Local"/>, it is converted to UTC before comparison.
    ///     <see cref="DateTimeKind.Unspecified"/> is treated as UTC.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPast(DateTime value)
    {
        var utcValue = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
        return utcValue < DateTime.UtcNow;
    }

    /// <summary>
    ///     Returns true if the DateTime is in the future.
    /// </summary>
    /// <remarks>
    ///     If <paramref name="value"/> has <see cref="DateTimeKind.Local"/>, it is converted to UTC before comparison.
    ///     <see cref="DateTimeKind.Unspecified"/> is treated as UTC.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsFuture(DateTime value)
    {
        var utcValue = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
        return utcValue > DateTime.UtcNow;
    }

    /// <summary>
    ///     Returns true if the DateTimeOffset is in the past.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsPast(DateTimeOffset value)
    {
        return value < DateTimeOffset.UtcNow;
    }

    /// <summary>
    ///     Returns true if the DateTimeOffset is in the future.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsFuture(DateTimeOffset value)
    {
        return value > DateTimeOffset.UtcNow;
    }

    /// <summary>
    ///     Returns true if the DateTime is not default (DateTime.MinValue).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNotDefault(DateTime value)
    {
        return value != default;
    }

    /// <summary>
    ///     Returns true if the DateTimeOffset is not default.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNotDefault(DateTimeOffset value)
    {
        return value != default;
    }

    /// <summary>
    ///     Returns true if the struct value is not default.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsNotDefault<T>(T value) where T : struct
    {
        return !EqualityComparer<T>.Default.Equals(value, default);
    }

    /// <summary>
    ///     Returns true if both values are equal.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool AreEqual<T>(T? value1, T? value2) where T : IEquatable<T>?
    {
        return EqualityComparer<T>.Default.Equals(value1, value2);
    }

    /// <summary>
    ///     Returns true if the values are not equal.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool AreNotEqual<T>(T? value1, T? value2) where T : IEquatable<T>?
    {
        return !EqualityComparer<T>.Default.Equals(value1, value2);
    }

}