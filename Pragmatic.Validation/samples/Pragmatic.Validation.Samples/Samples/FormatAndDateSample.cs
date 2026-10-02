namespace Pragmatic.Validation.Samples.Samples;

/// <summary>
///     Format validation ([Regex], [Guid], [OneOf]), date validation ([FutureDate], [PastDate]),
///     enum validation ([ValidEnum]), and custom message keys.
/// </summary>
public static class FormatAndDateSample
{
    public static void Run()
    {
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine("3. Format, Date & Enum Validation");
        Console.WriteLine("═══════════════════════════════════════════════════════════════");
        Console.WriteLine();

        ShowFormatValidation();
        ShowDateValidation();
        ShowEnumAndOneOf();

        Console.WriteLine();
    }

    private static void ShowFormatValidation()
    {
        Console.WriteLine("  3.1 [Regex] + [Guid] — Format pattern matching");
        Console.WriteLine("  -------------------------------------------------");

        var badFormat = new ScheduleEventRequest
        {
            Title = "Tech Conference 2025",
            StartDate = DateTime.UtcNow.AddDays(30),
            EndDate = DateTime.UtcNow.AddDays(31),
            Category = EventCategory.Conference,
            EventCode = "INVALID-CODE", // Doesn't match ^EVT-\d{4}-\d{3}$
            VenueId = "not-a-guid"       // Invalid GUID
        };

        var result = badFormat.Validate();
        Console.WriteLine($"    EventCode=\"{badFormat.EventCode}\" (expected EVT-YYYY-NNN)");
        Console.WriteLine($"    VenueId=\"{badFormat.VenueId}\" (expected valid GUID)");
        foreach (var issue in result.Issues.Where(i =>
                     i.PropertyPath is "EventCode" or "VenueId"))
            Console.WriteLine($"      - {issue.PropertyPath}: {issue.MessageKey}");

        // Valid formats pass silently — the point is the invalid case above
        Console.WriteLine("    Note: valid formats (EVT-2025-001, valid GUID) pass without issues.");
        Console.WriteLine();
    }

    private static void ShowDateValidation()
    {
        Console.WriteLine("  3.2 [FutureDate] + [PastDate] — Temporal constraints");
        Console.WriteLine("  ------------------------------------------------------");

        var badDates = new ScheduleEventRequest
        {
            Title = "Past Event",
            StartDate = DateTime.UtcNow.AddDays(-5), // In the past!
            EndDate = DateTime.UtcNow.AddDays(-4),
            Category = EventCategory.Meetup,
            RegistrationOpenedAt = DateTime.UtcNow.AddDays(10) // In the future!
        };

        var result = badDates.Validate();
        Console.WriteLine($"    StartDate=past (should be future) → fails");
        Console.WriteLine($"    RegistrationOpenedAt=future (should be past) → fails");
        foreach (var issue in result.Issues.Where(i =>
                     i.PropertyPath is "StartDate" or "RegistrationOpenedAt"))
            Console.WriteLine($"      - {issue.PropertyPath}: {issue.MessageKey}");
        Console.WriteLine();
    }

    private static void ShowEnumAndOneOf()
    {
        Console.WriteLine("  3.3 [ValidEnum] + [OneOf] — Allowed values");
        Console.WriteLine("  ---------------------------------------------");

        var badEnum = new ScheduleEventRequest
        {
            Title = "Mystery Event",
            StartDate = DateTime.UtcNow.AddDays(30),
            EndDate = DateTime.UtcNow.AddDays(31),
            Category = (EventCategory)999, // Invalid enum value!
            Format = "telepathy"            // Not in [OneOf("online", "in-person", "hybrid")]
        };

        var result = badEnum.Validate();
        Console.WriteLine($"    Category={(int)badEnum.Category} (invalid enum value)");
        Console.WriteLine($"    Format=\"{badEnum.Format}\" (not in: online, in-person, hybrid)");
        foreach (var issue in result.Issues.Where(i =>
                     i.PropertyPath is "Category" or "Format"))
            Console.WriteLine($"      - {issue.PropertyPath}: {issue.MessageKey}");
        Console.WriteLine();
    }
}
