using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Temporal.AspNetCore.ModelBinding;
using Pragmatic.Temporal.Context;
using Pragmatic.Temporal.Testing;

namespace Pragmatic.Temporal.AspNetCore.Tests;

public class DateTimeOffsetTemporalModelBinderTests
{
    private static DefaultModelBindingContext CreateContext(Type modelType, string? value)
    {
        var services = new ServiceCollection();
        services.AddSingleton<TemporalContext>(TestTemporalContext.ForRome(
            new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.FromHours(2))));

        var provider = new FakeValueProvider();
        if (value is not null)
            provider.With("at", value);

        return new DefaultModelBindingContext
        {
            ModelMetadata = new EmptyModelMetadataProvider().GetMetadataForType(modelType),
            ModelName = "at",
            ModelState = new ModelStateDictionary(),
            ValueProvider = provider,
            ActionContext = new ActionContext
            {
                HttpContext = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() }
            }
        };
    }

    [Fact]
    public async Task AsUtc_ValueWithoutOffset_IsInterpretedAsUtc()
    {
        var context = CreateContext(typeof(DateTimeOffset), "2026-06-01T12:00:00");

        await new DateTimeOffsetTemporalModelBinder(TemporalInputConversion.AsUtc).BindModelAsync(context);

        var bound = Assert.IsType<DateTimeOffset>(context.Result.Model);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero), bound);
    }

    [Fact]
    public async Task FromClientTimezone_ValueWithoutOffset_IsInterpretedInClientZone()
    {
        // Client zone is Europe/Rome (UTC+2 in June): wall 12:00 → 10:00Z.
        var context = CreateContext(typeof(DateTimeOffset), "2026-06-01T12:00:00");

        await new DateTimeOffsetTemporalModelBinder(TemporalInputConversion.FromClientTimezone)
            .BindModelAsync(context);

        var bound = Assert.IsType<DateTimeOffset>(context.Result.Model);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 10, 0, 0, TimeSpan.Zero), bound.ToUniversalTime());
    }

    [Fact]
    public async Task AnyConversion_ValueWithExplicitOffset_UsesTheOffset()
    {
        // The wire value carries its own offset: interpretation attributes must not override it.
        var context = CreateContext(typeof(DateTimeOffset), "2026-06-01T12:00:00+04:00");

        await new DateTimeOffsetTemporalModelBinder(TemporalInputConversion.FromClientTimezone)
            .BindModelAsync(context);

        var bound = Assert.IsType<DateTimeOffset>(context.Result.Model);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 8, 0, 0, TimeSpan.Zero), bound.ToUniversalTime());
    }

    [Fact]
    public async Task DateTimeModel_ReceivesUtcKind()
    {
        var context = CreateContext(typeof(DateTime), "2026-06-01T12:00:00");

        await new DateTimeOffsetTemporalModelBinder(TemporalInputConversion.FromClientTimezone)
            .BindModelAsync(context);

        var bound = Assert.IsType<DateTime>(context.Result.Model);
        Assert.Equal(DateTimeKind.Utc, bound.Kind);
        Assert.Equal(new DateTime(2026, 6, 1, 10, 0, 0, DateTimeKind.Utc), bound);
    }

    [Fact]
    public async Task InvalidValue_FailsWithModelError()
    {
        var context = CreateContext(typeof(DateTimeOffset), "not-a-date");

        await new DateTimeOffsetTemporalModelBinder(TemporalInputConversion.AsUtc).BindModelAsync(context);

        Assert.Equal(ModelBindingResult.Failed(), context.Result);
        Assert.True(context.ModelState.ErrorCount > 0);
    }

    [Fact]
    public async Task EmptyOnNullable_BindsNull()
    {
        var context = CreateContext(typeof(DateTimeOffset?), "");

        await new DateTimeOffsetTemporalModelBinder(TemporalInputConversion.AsUtc).BindModelAsync(context);

        Assert.True(context.Result.IsModelSet);
        Assert.Null(context.Result.Model);
    }
}
