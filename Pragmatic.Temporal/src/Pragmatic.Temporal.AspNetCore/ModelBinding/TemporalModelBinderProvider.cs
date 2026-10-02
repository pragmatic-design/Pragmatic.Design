using Microsoft.AspNetCore.Mvc.ModelBinding;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.AspNetCore.ModelBinding;

/// <summary>
///     Model binder provider for Pragmatic.Temporal types.
/// </summary>
public sealed class TemporalModelBinderProvider : IModelBinderProvider
{
    /// <inheritdoc />
    public IModelBinder? GetBinder(
        ModelBinderProviderContext context)
    {
        var modelType = context.Metadata.UnderlyingOrModelType;

        if (modelType == typeof(LocalDate))
            return new LocalDateModelBinder();
        if (modelType == typeof(LocalTime))
            return new LocalTimeModelBinder();
        if (modelType == typeof(LocalDateTime))
            return new LocalDateTimeModelBinder();

        return null;
    }
}
