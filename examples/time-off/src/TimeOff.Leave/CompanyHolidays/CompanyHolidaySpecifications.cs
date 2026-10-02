namespace TimeOff.Leave.Entities;

/// <summary>The rules a company holiday is read by, beside the generated <c>ById</c>.</summary>
public static partial class CompanyHolidaySpecifications
{
    /// <summary>On a day of <c>[from, to]</c>, both included.</summary>
    public static Specification<CompanyHoliday> Between(DateOnly from, DateOnly to)
        => Spec<CompanyHoliday>.Where(h => h.Date >= from && h.Date <= to);
}
