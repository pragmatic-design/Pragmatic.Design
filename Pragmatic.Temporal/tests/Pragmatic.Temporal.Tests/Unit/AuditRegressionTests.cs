using Pragmatic.Testing.Assertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Temporal.AspNetCore;
using Pragmatic.Temporal.AspNetCore.Detection;
using Pragmatic.Temporal.AspNetCore.Middleware;
using Pragmatic.Temporal.Calculator;
using Pragmatic.Temporal.Clock;
using Pragmatic.Temporal.Context;
using Pragmatic.Temporal.EntityFrameworkCore.QueryExtensions;
using Pragmatic.Temporal.Holidays;
using Pragmatic.Temporal.Types;
using Xunit;

namespace Pragmatic.Temporal.Tests.Unit;

/// <summary>
///     Pins cron range validation, holiday de-duplication, the zone/offset check on
///     <c>ZonedDateTime</c>, invalid-time-zone handling, the DST options reaching the context,
///     and the <c>AsOf</c> query extension.
/// </summary>
public class AuditRegressionTests
{
    #region Cron parsing rejects out-of-range numeric values

    [Theory]
    [InlineData("0 0 1 13 *")] // month 13
    [InlineData("60 * * * *")] // minute 60
    [InlineData("* 24 * * *")] // hour 24
    [InlineData("0 0 32 * *")] // day-of-month 32
    [InlineData("0 0 * * 8")] // day-of-week 8
    [InlineData("0 0 * 1-13 *")] // range endpoint out of range
    [InlineData("75/5 * * * *")] // step start out of range
    public void Cron_OutOfRangeNumericValue_FailsToParse(string expression)
    {
        CronExpression.TryParse(expression, out _).Should().BeFalse();
        Assert.Throws<FormatException>(() => CronExpression.Parse(expression));
    }

    [Theory]
    [InlineData("0 0 1 12 *")]
    [InlineData("59 * * * *")]
    [InlineData("0 0 * * 7")] // 7 is a valid Sunday alias
    [InlineData("0 0 * * 0-6")]
    public void Cron_InRangeNumericValue_ParsesSuccessfully(string expression)
    {
        CronExpression.TryParse(expression, out var result).Should().BeTrue();
        result.Should().NotBeNull();
    }

    #endregion

    #region Duplicate holidays on the same date are not over-counted

    private sealed class DuplicateHolidayProvider : IHolidayProvider
    {
        private readonly Holiday[] _holidays;

        public DuplicateHolidayProvider(params Holiday[] holidays) => _holidays = holidays;

        public IEnumerable<string> SupportedCountries => ["IT"];

        public IEnumerable<Holiday> GetHolidays(int year, string countryCode)
            => _holidays.Where(h => h.Date.Year == year);

        public IEnumerable<Holiday> GetHolidays(int year, string countryCode, string? regionCode)
            => GetHolidays(year, countryCode);

        public bool IsHoliday(LocalDate date, string countryCode)
            => _holidays.Any(h => h.Date == date);
    }

    [Fact]
    public void CountBusinessDays_DuplicateHolidaysSameDate_CountedOnce()
    {
        // Wednesday Jan 17, 2024 is a weekday holiday, supplied twice with different Name/Type.
        var holidayDate = new LocalDate(2024, 1, 17);
        var provider = new DuplicateHolidayProvider(
            new Holiday(holidayDate, "Festival", HolidayType.Public),
            new Holiday(holidayDate, "Bank Closure", HolidayType.Bank));
        var calculator = new TemporalCalculator(provider);

        var from = new LocalDate(2024, 1, 15); // Monday
        var to = new LocalDate(2024, 1, 22); // Monday

        // 5 weekday business days minus exactly 1 holiday (not 2).
        calculator.CountBusinessDays(from, to, "IT").Should().Be(4);
    }

    #endregion

    #region ZonedDateTime rejects offset/zone mismatch

    [Fact]
    public void ZonedDateTime_ExplicitOffsetMatchesZone_Parses()
    {
        // Europe/Rome is +01:00 in January (standard time).
        ZonedDateTime.TryParse("2024-01-15T10:30:00+01:00[Europe/Rome]", out var result)
            .Should().BeTrue();
        result.ZoneId.Should().Contain("Rome");
    }

    [Fact]
    public void ZonedDateTime_ExplicitOffsetMismatchesZone_FailsToParse()
    {
        // +05:00 is never a valid offset for Europe/Rome at this wall time.
        ZonedDateTime.TryParse("2024-01-15T10:30:00+05:00[Europe/Rome]", out _)
            .Should().BeFalse();
    }

    [Fact]
    public void ZonedDateTime_NoExplicitOffsetWithZone_StillParses()
    {
        // No offset supplied → nothing to validate against the zone.
        ZonedDateTime.TryParse("2024-01-15T10:30:00[Europe/Rome]", out var result)
            .Should().BeTrue();
        result.ZoneId.Should().Contain("Rome");
    }

    #endregion

    #region ThrowOnInvalidTimeZone is honored

    private static HttpContext BuildContext(
        TemporalOptions options,
        TemporalAspNetCoreOptions aspOptions,
        Action<HttpContext> configureRequest)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IClock>(SystemClock.Instance);
        services.AddSingleton<IOptions<TemporalOptions>>(Options.Create(options));
        services.AddSingleton<IOptions<TemporalAspNetCoreOptions>>(Options.Create(aspOptions));

        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        configureRequest(context);
        return context;
    }

    [Fact]
    public async Task Middleware_InvalidTimeZone_ThrowOnInvalid_Throws()
    {
        var options = new TemporalOptions { ThrowOnInvalidTimeZone = true };
        var aspOptions = new TemporalAspNetCoreOptions
        {
            DetectionStrategies = [new HeaderTimeZoneStrategy()]
        };
        var middleware = new TemporalContextMiddleware(_ => Task.CompletedTask);
        var context = BuildContext(options, aspOptions,
            c => c.Request.Headers["X-Timezone"] = "Not/AZone");

        await Assert.ThrowsAsync<TimeZoneNotFoundException>(() => middleware.InvokeAsync(context));
    }

    [Fact]
    public async Task Middleware_InvalidTimeZone_NoThrow_FallsBackToDefault()
    {
        var options = new TemporalOptions { ThrowOnInvalidTimeZone = false };
        var aspOptions = new TemporalAspNetCoreOptions
        {
            DetectionStrategies = [new HeaderTimeZoneStrategy()]
        };
        var middleware = new TemporalContextMiddleware(_ => Task.CompletedTask);
        var context = BuildContext(options, aspOptions,
            c => c.Request.Headers["X-Timezone"] = "Not/AZone");

        await middleware.InvokeAsync(context);

        var temporalContext = (TemporalContext)context.Items[typeof(TemporalContext)]!;
        temporalContext.ClientTimeZone.Should().Be(options.DefaultTimeZone);
    }

    [Fact]
    public async Task Middleware_NoTimeZoneSupplied_ThrowOnInvalid_DoesNotThrow()
    {
        // Absent input must NOT be treated as invalid, even when ThrowOnInvalidTimeZone is true.
        var options = new TemporalOptions { ThrowOnInvalidTimeZone = true };
        var aspOptions = new TemporalAspNetCoreOptions
        {
            DetectionStrategies = [new HeaderTimeZoneStrategy()]
        };
        var middleware = new TemporalContextMiddleware(_ => Task.CompletedTask);
        var context = BuildContext(options, aspOptions, _ => { });

        await middleware.InvokeAsync(context);

        var temporalContext = (TemporalContext)context.Items[typeof(TemporalContext)]!;
        temporalContext.ClientTimeZone.Should().Be(options.DefaultTimeZone);
    }

    #endregion

    #region TemporalOptions DST/policy knobs flow into the context

    [Fact]
    public async Task Middleware_FlowsOptionPoliciesIntoContext()
    {
        var options = new TemporalOptions
        {
            AmbiguousTimeHandling = AmbiguousTimePolicy.UseDaylightTime,
            NonExistentTimeHandling = NonExistentTimePolicy.ThrowException,
            FirstDayOfWeek = DayOfWeek.Sunday,
            DefaultCountryCode = "IT"
        };
        var aspOptions = new TemporalAspNetCoreOptions { DetectionStrategies = [] };
        var middleware = new TemporalContextMiddleware(_ => Task.CompletedTask);
        var context = BuildContext(options, aspOptions, _ => { });

        await middleware.InvokeAsync(context);

        var temporalContext = (TemporalContext)context.Items[typeof(TemporalContext)]!;
        temporalContext.AmbiguousTimeHandling.Should().Be(AmbiguousTimePolicy.UseDaylightTime);
        temporalContext.NonExistentTimeHandling.Should().Be(NonExistentTimePolicy.ThrowException);
        temporalContext.FirstDayOfWeek.Should().Be(DayOfWeek.Sunday);
        temporalContext.DefaultCountryCode.Should().Be("IT");
    }

    [Fact]
    public void TemporalContext_DefaultPolicies_MatchHistoricalBehavior()
    {
        var context = TemporalContext.Utc();
        context.AmbiguousTimeHandling.Should().Be(AmbiguousTimePolicy.UseStandardTime);
        context.NonExistentTimeHandling.Should().Be(NonExistentTimePolicy.ShiftForward);
        context.FirstDayOfWeek.Should().Be(DayOfWeek.Monday);
    }

    #endregion

    #region AsOf convenience query extension

    private sealed record AuditRow(DateTimeOffset CreatedAt);

    [Fact]
    public void AsOf_KeepsRecordsAtOrBeforeMoment()
    {
        var moment = new DateTimeOffset(2024, 6, 15, 12, 0, 0, TimeSpan.Zero);
        var rows = new[]
        {
            new AuditRow(moment.AddDays(-1)),
            new AuditRow(moment),
            new AuditRow(moment.AddDays(1))
        }.AsQueryable();

        var result = rows.AsOf(x => x.CreatedAt, moment).ToList();

        result.Should().HaveCount(2);
        result.Should().OnlyContain(r => r.CreatedAt <= moment);
    }

    #endregion
}
