namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a string is not null, empty, or whitespace only.
/// </summary>
/// <remarks>
///     <para>
///         This is stricter than <see cref="RequiredAttribute" /> as it also rejects
///         strings containing only whitespace characters (spaces, tabs, newlines).
///     </para>
///     <para>
///         Non-string values always pass (the attribute is string-specific).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record CreateCommentRequest
/// {
///     [NotWhiteSpace]  // "   " is invalid
///     public string Content { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class NotWhiteSpaceAttribute : ValidationAttribute
{
    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.notwhitespace";

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        return value switch
        {
            null => true, // Null check is [Required]'s job
            string s => !string.IsNullOrWhiteSpace(s),
            _ => true
        };
    }
}