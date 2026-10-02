using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Temporal.Context;
using Pragmatic.Temporal.Extensions;
using Pragmatic.Temporal.Timezone;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.Tests.Unit;

/// <summary>
///     Regression tests for the TemporalOptions split-brain bug: builder configuration
///     (services.Configure), the AddPragmaticTemporal delegate, and every reader
///     (bare singleton, IOptions) must all observe the same instance.
/// </summary>
public class TemporalOptionsConfigurationTests
{
    [Fact]
    public void ConfigureTemporalOptions_ReachesScopedTemporalContext()
    {
        // What TemporalBuilder.UseDefaultTimeZone does under the hood.
        var services = new ServiceCollection();
        services.AddPragmaticTemporal();

        var rome = TimeZoneResolver.GetTimeZone("Europe/Rome");
        services.Configure<TemporalOptions>(o => o.DefaultTimeZone = rome);

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TemporalContext>();

        Assert.Equal(rome.Id, ctx.ClientTimeZone.Id);
    }

    [Fact]
    public void AddPragmaticTemporalDelegate_ReachesIOptions()
    {
        // What the ASP.NET Core middleware reads.
        var services = new ServiceCollection();
        var rome = TimeZoneResolver.GetTimeZone("Europe/Rome");
        services.AddPragmaticTemporal(o => o.DefaultTimeZone = rome);

        using var sp = services.BuildServiceProvider();
        var viaIOptions = sp.GetRequiredService<IOptions<TemporalOptions>>().Value;

        Assert.Equal(rome.Id, viaIOptions.DefaultTimeZone.Id);
    }

    [Fact]
    public void BareSingletonAndIOptions_AreTheSameInstance()
    {
        var services = new ServiceCollection();
        services.AddPragmaticTemporal();

        using var sp = services.BuildServiceProvider();
        var bare = sp.GetRequiredService<TemporalOptions>();
        var viaIOptions = sp.GetRequiredService<IOptions<TemporalOptions>>().Value;

        Assert.Same(viaIOptions, bare);
    }

    [Fact]
    public void ScopedTemporalContext_PropagatesPoliciesFromOptions()
    {
        var services = new ServiceCollection();
        services.AddPragmaticTemporal(o =>
        {
            o.NonExistentTimeHandling = NonExistentTimePolicy.ThrowException;
            o.AmbiguousTimeHandling = AmbiguousTimePolicy.ThrowException;
            o.FirstDayOfWeek = DayOfWeek.Sunday;
            o.DefaultCountryCode = "IT";
        });

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var ctx = scope.ServiceProvider.GetRequiredService<TemporalContext>();

        Assert.Equal(NonExistentTimePolicy.ThrowException, ctx.NonExistentTimeHandling);
        Assert.Equal(AmbiguousTimePolicy.ThrowException, ctx.AmbiguousTimeHandling);
        Assert.Equal(DayOfWeek.Sunday, ctx.FirstDayOfWeek);
        Assert.Equal("IT", ctx.DefaultCountryCode);
    }

    [Fact]
    public void DelegateAndConfigure_ComposeOnTheSameInstance()
    {
        // Both configuration paths used together: last writer per property wins,
        // nothing is silently dropped.
        var services = new ServiceCollection();
        var rome = TimeZoneResolver.GetTimeZone("Europe/Rome");
        var london = TimeZoneResolver.GetTimeZone("Europe/London");

        services.AddPragmaticTemporal(o => o.DefaultTimeZone = rome);
        services.Configure<TemporalOptions>(o => o.BusinessTimeZone = london);

        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<TemporalOptions>();

        Assert.Equal(rome.Id, options.DefaultTimeZone.Id);
        Assert.Equal(london.Id, options.BusinessTimeZone.Id);
    }
}
