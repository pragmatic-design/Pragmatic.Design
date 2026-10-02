using Pragmatic.Validation.Attributes;

namespace Pragmatic.Validation.Samples;

/// <summary>
///     A realistic DTO for creating a user.
///     The source generator will produce a Validate() method implementing ISyncValidator.
/// </summary>
public partial record CreateUserRequest
{
    [Required, Email, MaxLength(255)]
    public required string Email { get; init; }

    [Required, MinLength(2), MaxLength(100)]
    public required string Name { get; init; }

    [Range(18, 120)]
    public int? Age { get; init; }

    [Phone]
    public string? PhoneNumber { get; init; }

    [Url]
    public string? Website { get; init; }
}
