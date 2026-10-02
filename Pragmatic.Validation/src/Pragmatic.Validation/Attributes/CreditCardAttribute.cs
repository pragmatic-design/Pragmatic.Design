namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a string is a valid credit card number using the Luhn algorithm.
/// </summary>
/// <remarks>
///     <para>
///         Validates the format and checksum using the Luhn algorithm (ISO/IEC 7812-1).
///         This validates the number structure, not whether the card is active or has funds.
///     </para>
///     <para>
///         Null or empty values pass validation (use <see cref="RequiredAttribute" /> for null checks).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record PaymentRequest
/// {
///     [Required]
///     [CreditCard]
///     public string CardNumber { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class CreditCardAttribute : ValidationAttribute
{
    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.creditcard";

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is not string cardNumber)
            return true;

        return IsValidCreditCard(cardNumber);
    }

    /// <summary>
    ///     Validates that a string is a valid credit card number.
    /// </summary>
    /// <param name="cardNumber">The credit card number to validate.</param>
    /// <returns><c>true</c> if the credit card number is valid or empty; otherwise, <c>false</c>.</returns>
    /// <remarks>
    ///     This method is used by the source generator to inline validation.
    /// </remarks>
    public static bool IsValidCreditCard(string? cardNumber)
    {
        if (string.IsNullOrEmpty(cardNumber))
            return true;

        // Guard against adversarial input: card numbers have at most 19 digits + separators;
        // reject inputs that far exceed this before stackalloc to prevent stack overflow.
        if (cardNumber.Length > 64)
            return false;

        // Remove spaces and hyphens
        Span<char> buffer = stackalloc char[cardNumber.Length];
        var pos = 0;
        foreach (var c in cardNumber)
            if (char.IsDigit(c))
                buffer[pos++] = c;
        var cleanNumber = new string(buffer[..pos]);

        // Card numbers are typically 13-19 digits
        if (cleanNumber.Length < 13 || cleanNumber.Length > 19)
            return false;

        // Luhn algorithm
        return IsValidLuhn(cleanNumber);
    }

    private static bool IsValidLuhn(string number)
    {
        var sum = 0;
        var alternate = false;

        for (var i = number.Length - 1; i >= 0; i--)
        {
            var digit = number[i] - '0';

            if (digit < 0 || digit > 9)
                return false;

            if (alternate)
            {
                digit *= 2;
                if (digit > 9)
                    digit -= 9;
            }

            sum += digit;
            alternate = !alternate;
        }

        return sum % 10 == 0;
    }
}