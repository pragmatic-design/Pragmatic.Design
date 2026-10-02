using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Temporal.Clock;
using Pragmatic.Temporal.Context;
using Pragmatic.Temporal.Extensions;
using Pragmatic.Temporal.Holidays;
using Pragmatic.Temporal.Timezone;

namespace Pragmatic.Temporal;

/// <summary>
///     Builder for configuring Temporal module via <c>UseTemporal()</c>.
/// </summary>
public sealed class TemporalBuilder
{
    private readonly IServiceCollection _services;

    internal TemporalBuilder(IServiceCollection services)
    {
        _services = services;
    }

    /// <summary>
    ///     Gets the service collection.
    /// </summary>
    public IServiceCollection Services => _services;

    /// <summary>
    ///     Sets the default timezone for the application.
    /// </summary>
    /// <param name="timeZone">The timezone.</param>
    /// <returns>This builder for chaining.</returns>
    public TemporalBuilder UseDefaultTimeZone(TimeZoneInfo timeZone)
    {
        _services.Configure<TemporalOptions>(options => options.DefaultTimeZone = timeZone);
        return this;
    }

    /// <summary>
    ///     Sets the default timezone by IANA ID (e.g., "Europe/Rome").
    /// </summary>
    /// <param name="timeZoneId">The timezone ID.</param>
    /// <returns>This builder for chaining.</returns>
    public TemporalBuilder UseDefaultTimeZone(string timeZoneId)
    {
        return UseDefaultTimeZone(TimeZoneResolver.GetTimeZone(timeZoneId));
    }

    /// <summary>
    ///     Sets the business timezone.
    /// </summary>
    /// <param name="timeZone">The business timezone.</param>
    /// <returns>This builder for chaining.</returns>
    public TemporalBuilder UseBusinessTimeZone(TimeZoneInfo timeZone)
    {
        _services.Configure<TemporalOptions>(options => options.BusinessTimeZone = timeZone);
        return this;
    }

    /// <summary>
    ///     Sets the business timezone by IANA ID.
    /// </summary>
    /// <param name="timeZoneId">The timezone ID.</param>
    /// <returns>This builder for chaining.</returns>
    public TemporalBuilder UseBusinessTimeZone(string timeZoneId)
    {
        return UseBusinessTimeZone(TimeZoneResolver.GetTimeZone(timeZoneId));
    }

    /// <summary>
    ///     Registers a custom holiday provider.
    /// </summary>
    /// <typeparam name="T">The holiday provider type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public TemporalBuilder UseHolidayProvider<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, IHolidayProvider
    {
        _services.UseHolidayProvider<T>();
        return this;
    }

    /// <summary>
    ///     Registers a custom holiday provider instance.
    /// </summary>
    /// <param name="provider">The holiday provider.</param>
    /// <returns>This builder for chaining.</returns>
    public TemporalBuilder UseHolidayProvider(IHolidayProvider provider)
    {
        _services.UseHolidayProvider(provider);
        return this;
    }

    /// <summary>
    ///     Replaces the default clock implementation.
    /// </summary>
    /// <typeparam name="T">The clock type.</typeparam>
    /// <returns>This builder for chaining.</returns>
    public TemporalBuilder UseClock<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>()
        where T : class, IClock
    {
        _services.UseClock<T>();
        return this;
    }

    /// <summary>
    ///     Replaces the default clock with a specific instance.
    /// </summary>
    /// <param name="clock">The clock instance.</param>
    /// <returns>This builder for chaining.</returns>
    public TemporalBuilder UseClock(IClock clock)
    {
        _services.UseClock(clock);
        return this;
    }
}
