using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pragmatic.Temporal.Json.Converters;

namespace Pragmatic.Temporal.AspNetCore.Tests;

public class HttpJsonOptionsRegistrationTests
{
    [Fact]
    public void AddPragmaticTemporalAspNetCore_ConfiguresMinimalApiJsonOptions()
    {
        var services = new ServiceCollection();
        services.AddPragmaticTemporalAspNetCore();

        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value;

        Assert.Contains(options.SerializerOptions.Converters, c => c is LocalDateConverter);
        Assert.Contains(options.SerializerOptions.Converters, c => c is ZonedDateTimeConverter);
        Assert.NotNull(options.SerializerOptions.TypeInfoResolver);
    }

    [Fact]
    public void AddPragmaticTemporalAspNetCore_ConfiguresMvcJsonOptions()
    {
        var services = new ServiceCollection();
        services.AddPragmaticTemporalAspNetCore();

        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>().Value;

        Assert.Contains(options.JsonSerializerOptions.Converters, c => c is LocalDateConverter);
        Assert.NotNull(options.JsonSerializerOptions.TypeInfoResolver);
    }
}
