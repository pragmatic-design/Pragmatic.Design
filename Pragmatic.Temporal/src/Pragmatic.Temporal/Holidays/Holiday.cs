using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Holidays;

/// <summary>
///     Represents a holiday.
/// </summary>
/// <param name="Date">The date of the holiday.</param>
/// <param name="Name">The name of the holiday.</param>
/// <param name="Type">The type of holiday.</param>
public readonly record struct Holiday(LocalDate Date, string Name, HolidayType Type)
{
    /// <summary>Creates a public holiday.</summary>
    public static Holiday Public(LocalDate date, string name)
    {
        return new Holiday(date, name, HolidayType.Public);
    }

    /// <summary>Creates a regional holiday.</summary>
    public static Holiday Regional(LocalDate date, string name)
    {
        return new Holiday(date, name, HolidayType.Regional);
    }

    /// <summary>Creates a bank holiday.</summary>
    public static Holiday Bank(LocalDate date, string name)
    {
        return new Holiday(date, name, HolidayType.Bank);
    }

    /// <summary>Creates an optional holiday.</summary>
    public static Holiday Optional(LocalDate date, string name)
    {
        return new Holiday(date, name, HolidayType.Optional);
    }
}

/// <summary>
///     The type of holiday.
/// </summary>
public enum HolidayType
{
    /// <summary>A national public holiday.</summary>
    Public,

    /// <summary>A regional or local holiday.</summary>
    Regional,

    /// <summary>A bank/financial holiday (banks closed).</summary>
    Bank,

    /// <summary>An optional or observance day.</summary>
    Optional
}