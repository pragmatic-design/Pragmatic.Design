namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a string is a valid GUID format.
/// </summary>
/// <remarks>
///     <para>
///         Accepts standard GUID formats: "D" (32 digits with hyphens), "N" (32 digits),
///         "B" (braces), and "P" (parentheses).
///     </para>
///     <para>
///         Null or empty values pass validation (use <see cref="RequiredAttribute" /> for null checks).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record AssignTaskRequest
/// {
///     [Required]
///     [Guid]
///     public string UserId { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class GuidAttribute : ValidationAttribute
{
    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.guid";

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is not string str)
            return true; // Null/non-string passes; null check is [Required]'s job

        return IsValidGuid(str);
    }

    /// <summary>
    ///     Validates that a string is a valid GUID format.
    /// </summary>
    /// <param name="value">The string to validate.</param>
    /// <returns><c>true</c> if the value is a valid GUID or empty; otherwise, <c>false</c>.</returns>
    /// <remarks>
    ///     This method is used by the source generator to inline validation.
    /// </remarks>
    public static bool IsValidGuid(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return true;

        return System.Guid.TryParse(value, out _);
    }
}
