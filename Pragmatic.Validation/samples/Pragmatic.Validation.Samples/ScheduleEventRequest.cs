using Pragmatic.Validation.Attributes;

namespace Pragmatic.Validation.Samples;

/// <summary>
///     Format, date, and enum validation.
///     Demonstrates [Regex], [FutureDate], [PastDate], [ValidEnum], [OneOf], [Guid].
/// </summary>
public partial record ScheduleEventRequest
{
    [Required, MinLength(3), MaxLength(200)]
    public required string Title { get; init; }

    // Must be in the future
    [FutureDate]
    public DateTime StartDate { get; init; }

    // Must be after StartDate
    [GreaterThanProperty(nameof(StartDate))]
    public DateTime EndDate { get; init; }

    // Valid enum value
    [ValidEnum]
    public EventCategory Category { get; init; }

    // Only specific values allowed
    [OneOf("online", "in-person", "hybrid")]
    public string? Format { get; init; }

    // Regex: alphanumeric event code (e.g., "EVT-2025-001")
    [Regex(@"^EVT-[0-9]{4}-[0-9]{3}$", MessageKey = "validation.event_code.format")]
    public string? EventCode { get; init; }

    // Valid GUID reference
    [Guid]
    public string? VenueId { get; init; }

    // Must be in the past
    [PastDate]
    public DateTime? RegistrationOpenedAt { get; init; }
}

public enum EventCategory
{
    Conference,
    Workshop,
    Meetup,
    Webinar
}
