namespace TimeOff.Leave.Enums;

/// <summary>
///     Why the company is closed on a day that is not a national holiday.
/// </summary>
public enum CompanyHolidayKind
{
    /// <summary>A closure the company decides: the bridge between a holiday and a weekend, August.</summary>
    Closure,

    /// <summary>The patron saint of the city the company is in.</summary>
    PatronSaint
}
