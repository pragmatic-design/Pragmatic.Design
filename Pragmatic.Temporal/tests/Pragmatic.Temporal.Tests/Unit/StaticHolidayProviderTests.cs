using Pragmatic.Testing.Assertions;
using Pragmatic.Temporal.Holidays;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Tests.Unit;

public class StaticHolidayProviderTests
{
    [Fact]
    public void SupportedCountries_MutationAfterRead_DoesNotAffectSnapshot()
    {
        var provider = new StaticHolidayProvider();
        provider.AddHoliday("IT", Holiday.Public(new LocalDate(2026, 12, 25), "Natale"));

        var snapshot = provider.SupportedCountries;
        provider.AddHoliday("US", Holiday.Public(new LocalDate(2026, 7, 4), "Independence Day"));

        snapshot.Should().BeEquivalentTo("IT");
        provider.SupportedCountries.Should().BeEquivalentTo("IT", "US");
    }

    [Fact]
    public void GetHolidays_RegionCode_IsIgnored()
    {
        var provider = new StaticHolidayProvider();
        provider.AddHoliday("IT", Holiday.Public(new LocalDate(2026, 12, 25), "Natale"));

        var withRegion = provider.GetHolidays(2026, "IT", "IT-25");
        var withoutRegion = provider.GetHolidays(2026, "IT");

        withRegion.Should().BeEquivalentTo(withoutRegion);
    }

    [Fact]
    public void IsHoliday_CaseInsensitiveCountry_Matches()
    {
        var provider = new StaticHolidayProvider();
        provider.AddHoliday("it", Holiday.Public(new LocalDate(2026, 12, 25), "Natale"));

        provider.IsHoliday(new LocalDate(2026, 12, 25), "IT").Should().BeTrue();
    }
}
