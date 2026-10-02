using Pragmatic.Testing.Assertions;
using Pragmatic.Temporal.Timezone;

namespace Pragmatic.Temporal.Tests.Unit;

public class TimeZoneResolverTests
{
    [Fact]
    public void GetTimeZone_IanaId_ResolvesZone()
    {
        var zone = TimeZoneResolver.GetTimeZone("Europe/Rome");

        zone.Should().NotBeNull();
        zone.BaseUtcOffset.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void GetTimeZone_WindowsId_ResolvesZone()
    {
        var zone = TimeZoneResolver.GetTimeZone("W. Europe Standard Time");

        zone.Should().NotBeNull();
        zone.BaseUtcOffset.Should().Be(TimeSpan.FromHours(1));
    }

    [Fact]
    public void GetTimeZone_InvalidId_Throws()
    {
        var act = () => TimeZoneResolver.GetTimeZone("Not/AZone");

        act.Should().Throw<TimeZoneNotFoundException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Invalid/Zone")]
    public void TryGetTimeZone_InvalidInput_ReturnsFalse(string? input)
    {
        var result = TimeZoneResolver.TryGetTimeZone(input, out var zone);

        result.Should().BeFalse();
        zone.Should().BeNull();
    }

    [Fact]
    public void TryGetTimeZone_ValidIana_ReturnsTrue()
    {
        var result = TimeZoneResolver.TryGetTimeZone("America/New_York", out var zone);

        result.Should().BeTrue();
        zone.Should().NotBeNull();
    }

    [Theory]
    [InlineData("Europe/Rome", true)]
    [InlineData("America/New_York", true)]
    [InlineData("UTC", true)]
    [InlineData("Nowhere/Nothing", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsValidTimezone_KnownAndUnknownIds_MatchesExpectation(string? id, bool expected)
    {
        TimeZoneResolver.IsValidTimezone(id).Should().Be(expected);
    }

    [Theory]
    [InlineData("Europe/Rome")]
    [InlineData("America/New_York")]
    public void GetIanaId_RoundTrip_PreservesIanaId(string ianaId)
    {
        var zone = TimeZoneResolver.GetTimeZone(ianaId);

        TimeZoneResolver.GetIanaId(zone).Should().Be(ianaId);
    }

    [Fact]
    public void GetWindowsId_FromWindowsZone_ReturnsWindowsId()
    {
        // Note: a zone resolved from an IANA id keeps the IANA id even on Windows,
        // and GetWindowsId returns it as-is (documented current behavior).
        var zone = TimeZoneResolver.GetTimeZone("W. Europe Standard Time");

        TimeZoneResolver.GetWindowsId(zone).Should().Be("W. Europe Standard Time");
    }

    [Theory]
    [InlineData("UTC")]
    [InlineData("utc")]
    [InlineData("Z")]
    public void GetTimeZone_UtcAliases_ResolveToUtc(string id)
    {
        var zone = TimeZoneResolver.GetTimeZone(id);

        zone.BaseUtcOffset.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void CommonZones_AreResolvable()
    {
        TimeZoneResolver.CommonZones.Utc.Should().NotBeNull();
        TimeZoneResolver.CommonZones.Rome.Should().NotBeNull();
        TimeZoneResolver.CommonZones.NewYork.Should().NotBeNull();
        TimeZoneResolver.CommonZones.London.Should().NotBeNull();
        TimeZoneResolver.CommonZones.Tokyo.Should().NotBeNull();
    }

    [Fact]
    public void GetTimeZone_SecondCall_ReturnsCachedInstance()
    {
        var first = TimeZoneResolver.GetTimeZone("Europe/Rome");
        var second = TimeZoneResolver.GetTimeZone("Europe/Rome");

        second.Should().BeSameAs(first);
    }
}
