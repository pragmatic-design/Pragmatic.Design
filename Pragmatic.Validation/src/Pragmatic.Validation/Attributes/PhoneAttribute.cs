using System.Text.RegularExpressions;

namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a string is a valid phone number format.
/// </summary>
/// <remarks>
///     <para>
///         Validates phone numbers in common formats including international format.
///         Allows: digits, spaces, hyphens, parentheses, and optional leading +.
///     </para>
///     <para>
///         For strict E.164 format, use <see cref="RegexAttribute" /> with pattern <c>^\+[1-9]\d{1,14}$</c>.
///     </para>
///     <para>
///         Null or empty values pass validation (use <see cref="RequiredAttribute" /> for null checks).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record ContactRequest
/// {
///     [Phone]
///     public string PhoneNumber { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class PhoneAttribute : ValidationAttribute
{
    // Allows: +1 234 567-8900, (234) 567-8900, 234-567-8900, +39 02 1234567, etc.
    private static readonly Regex PhoneRegex = new(
        @"^[\+]?[(]?[0-9]{1,4}[)]?[-\s\.]?[(]?[0-9]{1,4}[)]?[-\s\.]?[0-9]{1,9}([-\s\.]?[0-9]{1,9})*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.phone";

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is not string phone)
            return true;

        return IsValidPhone(phone);
    }

    /// <summary>
    ///     Validates that a string is a valid phone number format.
    /// </summary>
    /// <param name="phone">The phone string to validate.</param>
    /// <returns><c>true</c> if the phone is valid or empty; otherwise, <c>false</c>.</returns>
    /// <remarks>
    ///     This method is used by the source generator to inline validation.
    /// </remarks>
    public static bool IsValidPhone(string? phone)
    {
        if (string.IsNullOrEmpty(phone))
            return true;

        // Count digits inline — no buffer allocation. A `stackalloc char[phone.Length]` has no
        // length cap, so an adversarially long value (e.g. a multi-KB form field) could overflow
        // the stack (an uncatchable crash / DoS). We only need the digit count here.
        var digitCount = 0;
        foreach (var c in phone)
            if (char.IsDigit(c))
                digitCount++;
        if (digitCount is < 7 or > 15)
            return false;

        try
        {
            return PhoneRegex.IsMatch(phone);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}