using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Pragmatic.Temporal.AspNetCore.ModelBinding;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.AspNetCore.Tests;

public class LocalTemporalModelBinderTests
{
    private static DefaultModelBindingContext CreateContext(Type modelType, string name, string? value)
    {
        var provider = new FakeValueProvider();
        if (value is not null)
            provider.With(name, value);

        return new DefaultModelBindingContext
        {
            ModelMetadata = new EmptyModelMetadataProvider().GetMetadataForType(modelType),
            ModelName = name,
            ModelState = new ModelStateDictionary(),
            ValueProvider = provider,
            ActionContext = new ActionContext()
        };
    }

    [Fact]
    public async Task LocalDateBinder_ValidValue_Binds()
    {
        var context = CreateContext(typeof(LocalDate), "date", "2026-06-01");

        await new LocalDateModelBinder().BindModelAsync(context);

        Assert.True(context.Result.IsModelSet);
        Assert.Equal(new LocalDate(2026, 6, 1), context.Result.Model);
    }

    [Fact]
    public async Task LocalDateBinder_InvalidValue_FailsWithModelError()
    {
        var context = CreateContext(typeof(LocalDate), "date", "not-a-date");

        await new LocalDateModelBinder().BindModelAsync(context);

        Assert.Equal(ModelBindingResult.Failed(), context.Result);
        Assert.True(context.ModelState.ErrorCount > 0);
    }

    [Fact]
    public async Task LocalDateBinder_EmptyOnNullable_BindsNull()
    {
        var context = CreateContext(typeof(LocalDate?), "date", "");

        await new LocalDateModelBinder().BindModelAsync(context);

        Assert.True(context.Result.IsModelSet);
        Assert.Null(context.Result.Model);
    }

    [Fact]
    public async Task LocalTimeBinder_InvalidValue_FailsWithModelError()
    {
        var context = CreateContext(typeof(LocalTime), "time", "25:99");

        await new LocalTimeModelBinder().BindModelAsync(context);

        Assert.Equal(ModelBindingResult.Failed(), context.Result);
        Assert.True(context.ModelState.ErrorCount > 0);
    }

    [Fact]
    public async Task LocalDateTimeBinder_ValidValue_Binds()
    {
        var context = CreateContext(typeof(LocalDateTime), "at", "2026-06-01T14:30:00");

        await new LocalDateTimeModelBinder().BindModelAsync(context);

        Assert.True(context.Result.IsModelSet);
        Assert.Equal(new LocalDateTime(2026, 6, 1, 14, 30), context.Result.Model);
    }

    [Fact]
    public async Task LocalDateTimeBinder_InvalidValue_FailsWithModelError()
    {
        var context = CreateContext(typeof(LocalDateTime), "at", "garbage");

        await new LocalDateTimeModelBinder().BindModelAsync(context);

        Assert.Equal(ModelBindingResult.Failed(), context.Result);
        Assert.True(context.ModelState.ErrorCount > 0);
    }

    [Fact]
    public void Provider_MapsTemporalTypesIncludingNullables()
    {
        var provider = new TemporalModelBinderProvider();
        var metadataProvider = new EmptyModelMetadataProvider();

        Assert.NotNull(GetBinder(typeof(LocalDate)));
        Assert.NotNull(GetBinder(typeof(LocalDate?)));
        Assert.NotNull(GetBinder(typeof(LocalTime)));
        Assert.NotNull(GetBinder(typeof(LocalDateTime)));
        Assert.Null(GetBinder(typeof(DateTimeOffset)));

        IModelBinder? GetBinder(Type type)
        {
            return provider.GetBinder(new TestModelBinderProviderContext(metadataProvider.GetMetadataForType(type)));
        }
    }
}
