using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net.Mail;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace Pragmatic.Ensure;

/// <summary>
///     Static guard methods for parameter validation.
///     This partial contains string checks.
/// </summary>
public static partial class Ensure
{
    // Pre-compiled regex pattern with timeout to prevent ReDoS
    private static readonly Regex PhoneRegex = new(
        @"^[+]?[\d]{0,4}[-.\s]?\(?\d{1,4}\)?[-.\s]?\d{1,4}[-.\s]?\d{1,4}[-.\s]?\d{0,9}$",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(250));

    // Bounded cache for compiled regex patterns used by IsMatchInternal
    private const int MaxRegexCacheSize = 64;

    private static readonly ConcurrentDictionary<string, Regex> RegexCache = new();

    // Guards the count-check+add operation so the cache never exceeds MaxRegexCacheSize.
    private static readonly System.Threading.Lock RegexCacheLock = new();
    /// <summary>
    ///     Throws <see cref="ArgumentException" /> if the string is null or empty.
    ///     Returns the validated (non-null, non-empty) string for fluent assignment.
    /// </summary>
    /// <param name="value">The string to check.</param>
    /// <param name="paramName">The name of the parameter (auto-captured).</param>
    /// <returns>The validated non-null string.</returns>
    /// <exception cref="ArgumentException">Thrown when value is null or empty.</exception>
    /// <example>
    ///     <code>
    ///     _name = Ensure.ThrowIfNullOrEmpty(name);
    ///     </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ThrowIfNullOrEmpty(
        [NotNull] string? value,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
    {
        if (string.IsNullOrEmpty(value))
            throw new ArgumentException("Value cannot be null or empty.", paramName);
        return value;
    }

    /// <summary>
    ///     Throws <see cref="ArgumentException" /> if the string is null, empty, or whitespace.
    ///     Returns the validated (non-null, non-whitespace) string for fluent assignment.
    /// </summary>
    /// <param name="value">The string to check.</param>
    /// <param name="paramName">The name of the parameter (auto-captured).</param>
    /// <returns>The validated non-null string.</returns>
    /// <exception cref="ArgumentException">Thrown when value is null, empty, or whitespace.</exception>
    /// <example>
    ///     <code>
    ///     _name = Ensure.ThrowIfNullOrWhiteSpace(name);
    ///     </code>
    /// </example>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static string ThrowIfNullOrWhiteSpace(
        [NotNull] string? value,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Value cannot be null, empty, or whitespace.", paramName);
        return value;
    }

    /// <summary>
    ///     Throws <see cref="ArgumentException" /> if the string is longer than the specified maximum.
    /// </summary>
    /// <remarks>
    ///     Throws <see cref="ArgumentNullException" /> if value is null.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfLongerThan(
        string value,
        int maxLength,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
    {
        ThrowIfNull(value, paramName);
        if (value.Length > maxLength)
            throw new ArgumentException($"Value length must be at most {maxLength}. Actual: {value.Length}.",
                paramName);
    }

    /// <summary>
    ///     Throws <see cref="ArgumentException" /> if the string is shorter than the specified minimum.
    /// </summary>
    /// <remarks>
    ///     Throws <see cref="ArgumentNullException" /> if value is null.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfShorterThan(
        string value,
        int minLength,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
    {
        ThrowIfNull(value, paramName);
        if (value.Length < minLength)
            throw new ArgumentException($"Value length must be at least {minLength}. Actual: {value.Length}.",
                paramName);
    }

    /// <summary>
    ///     Throws <see cref="ArgumentNullException" /> if value is null.
    ///     Throws <see cref="ArgumentException" /> if the string length is not within the specified range.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfLengthOutOfRange(
        string value,
        int minLength,
        int maxLength,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
    {
        ThrowIfNull(value, paramName);
        if (value.Length < minLength || value.Length > maxLength)
            throw new ArgumentException(
                $"Value length must be between {minLength} and {maxLength}. Actual: {value.Length}.", paramName);
    }

    /// <summary>
    ///     Throws <see cref="ArgumentException" /> if the string contains the specified substring.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfContains(
        string value,
        string substring,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
    {
        ThrowIfNull(value, paramName);
        if (value.Contains(substring, StringComparison.Ordinal))
            throw new ArgumentException($"Value must not contain '{substring}'.", paramName);
    }

    /// <summary>
    ///     Throws <see cref="ArgumentException" /> if the string starts with the specified prefix.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfStartsWith(
        string value,
        string prefix,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
    {
        ThrowIfNull(value, paramName);
        if (value.StartsWith(prefix, StringComparison.Ordinal))
            throw new ArgumentException($"Value must not start with '{prefix}'.", paramName);
    }

    /// <summary>
    ///     Throws <see cref="ArgumentException" /> if the string ends with the specified suffix.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfEndsWith(
        string value,
        string suffix,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
    {
        ThrowIfNull(value, paramName);
        if (value.EndsWith(suffix, StringComparison.Ordinal))
            throw new ArgumentException($"Value must not end with '{suffix}'.", paramName);
    }

    /// <summary>
    ///     Throws <see cref="ArgumentException" /> if the string is not a valid email address.
    ///     Null values are allowed (null-safe).
    /// </summary>
    /// <remarks>
    ///     <b>Null-safe:</b> a null <paramref name="value"/> silently passes this check.
    ///     If you require a non-null email, call <see cref="ThrowIfNull{T}(T?, string?)" /> first.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfNotEmail(
        string? value,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
    {
        if (value is null)
            return;
        if (!IsEmailInternal(value))
            throw new ArgumentException("Value must be a valid email address.", paramName);
    }

    /// <summary>
    ///     Throws <see cref="ArgumentException" /> if the string does not match the specified regex pattern.
    ///     Null values are allowed (null-safe).
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfNotMatch(
        string? value,
        [StringSyntax(StringSyntaxAttribute.Regex)]
        string pattern,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
    {
        if (value is null)
            return;
        if (!IsMatchInternal(value, pattern, throwOnTimeout: true))
            throw new ArgumentException("Value does not match the required pattern.", paramName);
    }

    /// <summary>
    ///     Throws <see cref="ArgumentException" /> if the string is not a valid phone number.
    ///     Null values are allowed (null-safe).
    /// </summary>
    /// <remarks>
    ///     <b>Null-safe:</b> a null <paramref name="value"/> silently passes this check.
    ///     If you require a non-null phone number, call <see cref="ThrowIfNull{T}(T?, string?)" /> first.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfNotPhone(
        string? value,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
    {
        if (value is null)
            return;
        if (!IsPhoneInternal(value, throwOnTimeout: true))
            throw new ArgumentException("Value must be a valid phone number.", paramName);
    }

    /// <summary>
    ///     Throws <see cref="ArgumentException" /> if the string is not a valid URL.
    ///     Null values are allowed (null-safe).
    /// </summary>
    /// <remarks>
    ///     <b>Null-safe:</b> a null <paramref name="value"/> silently passes this check.
    ///     If you require a non-null URL, call <see cref="ThrowIfNull{T}(T?, string?)" /> first.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfNotUrl(
        string? value,
        bool requireHttps = false,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
    {
        if (value is null)
            return;
        if (!IsUrlInternal(value, requireHttps))
            throw new ArgumentException(
                requireHttps ? "Value must be a valid HTTPS URL." : "Value must be a valid URL.",
                paramName);
    }

    /// <summary>
    ///     Throws <see cref="ArgumentException" /> if the string is not a valid credit card number.
    ///     Validation uses the Luhn checksum (ISO/IEC 7812); spaces and hyphens are ignored.
    ///     Null values are allowed (null-safe).
    /// </summary>
    /// <remarks>
    ///     <b>Null-safe:</b> a null <paramref name="value"/> silently passes this check.
    ///     If you require a non-null card number, call <see cref="ThrowIfNull{T}(T?, string?)" /> first.
    ///     Validates the checksum only — it does not verify that the card exists or is active.
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void ThrowIfNotCreditCard(
        string? value,
        [CallerArgumentExpression(nameof(value))]
        string? paramName = null)
    {
        if (value is null)
            return;
        if (!IsCreditCardInternal(value))
            throw new ArgumentException("Value must be a valid credit card number.", paramName);
    }

    private static bool IsEmailInternal(string value)
    {
        try
        {
            // Use MailAddress parser for robust RFC-compliant validation
            var addr = new MailAddress(value);

            // MailAddress accepts "Display Name <email>" — reject that form
            return addr.Address == value;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool IsMatchInternal(string value, string pattern, bool throwOnTimeout)
    {
        try
        {
            var regex = GetOrCreateRegex(pattern);
            return regex.IsMatch(value);
        }
        catch (RegexMatchTimeoutException)
        {
            // ThrowIf* path surfaces the timeout as an ArgumentException; the Is* path must
            // honour the "Is* never throws" contract and treat a timeout as a non-match.
            if (!throwOnTimeout)
                return false;
            throw new ArgumentException(
                $"Regex pattern match timed out after 250ms — the pattern is too complex or the input is too long (length: {value.Length}).");
        }
    }

    private static Regex GetOrCreateRegex(string pattern)
    {
        if (RegexCache.TryGetValue(pattern, out var cached))
            return cached;

        var regex = new Regex(pattern, RegexOptions.Compiled, TimeSpan.FromMilliseconds(250));

        // Atomic count-check + add to prevent exceeding the limit under concurrent load.
        lock (RegexCacheLock)
        {
            if (RegexCache.Count < MaxRegexCacheSize)
                RegexCache.TryAdd(pattern, regex);
        }

        return regex;
    }

    private static bool IsPhoneInternal(string value, bool throwOnTimeout)
    {
        try
        {
            if (!PhoneRegex.IsMatch(value))
                return false;

            // After format check, verify E.164-compatible digit count (7-15 digits)
            var digitCount = 0;
            foreach (var c in value)
            {
                if (char.IsDigit(c))
                    digitCount++;
            }

            return digitCount is >= 7 and <= 15;
        }
        catch (RegexMatchTimeoutException)
        {
            // ThrowIf* path surfaces the timeout as an ArgumentException; the Is* path must
            // honour the "Is* never throws" contract and treat a timeout as a non-match.
            if (!throwOnTimeout)
                return false;
            throw new ArgumentException(
                $"Phone number regex match timed out after 250ms — the input is too long or malformed (length: {value.Length}).");
        }
    }

    private static bool IsUrlInternal(string value, bool requireHttps)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;

        if (requireHttps)
            return uri.Scheme == Uri.UriSchemeHttps;

        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    private static bool IsCreditCardInternal(string value)
    {
        // Luhn checksum (ISO/IEC 7812). Ignore spaces and hyphens; reject any other non-digit.
        // Accept 13-19 digit numbers (Visa/Mastercard/Amex/Discover/… range).
        var digitCount = 0;
        var sum = 0;
        var isSecond = false;

        // Iterate right-to-left so "every second digit" starts from the check digit.
        for (var i = value.Length - 1; i >= 0; i--)
        {
            var c = value[i];
            if (c is ' ' or '-')
                continue;
            if (!char.IsDigit(c))
                return false;

            var digit = c - '0';
            if (isSecond)
            {
                digit *= 2;
                if (digit > 9)
                    digit -= 9;
            }

            sum += digit;
            isSecond = !isSecond;
            digitCount++;
        }

        return digitCount is >= 13 and <= 19 && sum % 10 == 0;
    }

}