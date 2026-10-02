using System.Text.RegularExpressions;

namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a string is a valid email address format.
/// </summary>
/// <remarks>
///     <para>
///         Uses a practical regex pattern that covers most valid email formats.
///         For strict RFC 5322 compliance, use <see cref="RegexAttribute" /> with a custom pattern.
///     </para>
///     <para>
///         Null or empty values pass validation (use <see cref="RequiredAttribute" /> for null checks).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record CreateUserRequest
/// {
///     [Required]
///     [Email]
///     public string Email { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class EmailAttribute : ValidationAttribute
{
    // Practical email pattern - not RFC 5322 compliant but covers 99.9% of real emails
    private static readonly Regex EmailRegex = new(
        @"^[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.email";

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is not string email)
            return true; // Null/non-string passes; null check is [Required]'s job

        return IsValidEmail(email);
    }

    /// <summary>
    ///     Validates that a string is a valid email format.
    /// </summary>
    /// <param name="email">The email string to validate.</param>
    /// <returns><c>true</c> if the email is valid or empty; otherwise, <c>false</c>.</returns>
    /// <remarks>
    ///     This method is used by the source generator to inline validation.
    /// </remarks>
    public static bool IsValidEmail(string? email)
    {
        if (string.IsNullOrEmpty(email))
            return true;

        // Basic sanity checks before regex
        if (email.Length > 254) // Max email length per RFC
            return false;

        try
        {
            return EmailRegex.IsMatch(email);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}