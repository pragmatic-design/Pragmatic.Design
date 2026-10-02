using Pragmatic.Validation.Attributes;

namespace Pragmatic.Validation.Samples;

/// <summary>
///     Cross-property validation: password confirmation, conditional shipping address.
///     Demonstrates [EqualTo], [RequiredIf], [GreaterThanProperty].
/// </summary>
public partial record RegisterAccountRequest
{
    [Required, Email]
    public required string Email { get; init; }

    [Required, MinLength(8), MaxLength(64)]
    public required string Password { get; init; }

    // Must match Password
    [Required, EqualTo(nameof(Password))]
    public required string ConfirmPassword { get; init; }

    // If true, ShippingStreet and ShippingCity become required
    public bool NeedsShipping { get; init; }

    [RequiredIf(nameof(NeedsShipping), true)]
    public string? ShippingStreet { get; init; }

    [RequiredIf(nameof(NeedsShipping), true)]
    public string? ShippingCity { get; init; }

    // Age range: MinAge < MaxAge
    [Range(0, 120)]
    public int? MinAge { get; init; }

    [GreaterThanProperty(nameof(MinAge))]
    public int? MaxAge { get; init; }
}
