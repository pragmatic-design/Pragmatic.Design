namespace Showcase.Booking.Dtos;

/// <summary>
/// Request to register a new guest.
/// Demonstrates: validation attributes (Required, Email, Phone).
/// Entity creation uses <see cref="Showcase.Booking.Entities.Guest"/> factory method.
/// </summary>
public partial class RegisterGuestRequest
{
    [Required]
    [NotWhiteSpace]
    public string FirstName { get; init; } = "";

    [Required]
    [NotWhiteSpace]
    public string LastName { get; init; } = "";

    [Required]
    [Email]
    public string Email { get; init; } = "";

    [Phone]
    public string? Phone { get; init; }

    public string? Nationality { get; init; }
}
