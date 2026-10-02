namespace Showcase.Booking.Dtos;

/// <summary>
/// Guest DTO for display.
/// Demonstrates: MapFrom, GenerateProjection, MapIgnore.
/// </summary>
[MapFrom<Guest>]
[GenerateProjection]
public partial class GuestDto
{
    public Guid Id { get; init; }
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string Email { get; init; } = "";
    public string? Phone { get; init; }
    public string? Nationality { get; init; }
    public string PreferredLanguage { get; init; } = "";

    /// <summary>When the guest was registered. Trait-generated on the entity by [Auditable].</summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>When the guest was last changed, or null if never.</summary>
    public DateTimeOffset? UpdatedAt { get; init; }

    /// <summary>Computed display name — not mapped to entity.</summary>
    [MapIgnore]
    public string FullName => $"{FirstName} {LastName}";
}
