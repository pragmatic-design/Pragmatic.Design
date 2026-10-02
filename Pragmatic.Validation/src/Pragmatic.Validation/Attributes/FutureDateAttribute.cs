namespace Pragmatic.Validation.Attributes;

/// <summary>
///     Validates that a DateTime or DateTimeOffset is in the future.
/// </summary>
/// <remarks>
///     <para>
///         Compares against <see cref="ValidationTimeProvider.Current" /> for testability.
///         Uses <see cref="TimeProvider.GetUtcNow" /> instead of <see cref="DateTime.UtcNow" /> directly.
///     </para>
///     <para>
///         Null values pass validation (use <see cref="RequiredAttribute" /> for null checks).
///     </para>
/// </remarks>
/// <example>
///     <code>
/// public partial record ScheduleMeetingRequest
/// {
///     [Required]
///     [FutureDate]
///     public DateTime StartDate { get; init; }
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class FutureDateAttribute : ValidationAttribute
{
    /// <inheritdoc />
    public override string DefaultMessageKey => "validation.future_date";

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        var now = ValidationTimeProvider.Current.GetUtcNow();
        return value switch
        {
            null => true,
            // Normalize Kind before comparing to a UTC instant: a Local value is converted to UTC;
            // Unspecified is treated as UTC. Comparing the raw DateTime would be off by the local offset.
            DateTime dt => (dt.Kind == DateTimeKind.Local ? dt.ToUniversalTime() : dt) > now.UtcDateTime,
            DateTimeOffset dto => dto > now,
            DateOnly d => d > DateOnly.FromDateTime(now.UtcDateTime),
            _ => true
        };
    }
}
