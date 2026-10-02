using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Pragmatic.Temporal.Attributes;

namespace Pragmatic.Temporal.AspNetCore.ModelBinding;

/// <summary>
///     Provides <see cref="DateTimeOffsetTemporalModelBinder" /> for
///     <see cref="DateTimeOffset" /> / <see cref="DateTime" /> parameters and properties
///     decorated with a timezone conversion attribute (<see cref="AsUtcAttribute" />,
///     <see cref="FromClientTimezoneAttribute" />, <see cref="FromBusinessTimezoneAttribute" />).
///     Undecorated values keep the default MVC binding behavior.
/// </summary>
public sealed class TemporalConversionModelBinderProvider : IModelBinderProvider
{
    /// <inheritdoc />
    public IModelBinder? GetBinder(ModelBinderProviderContext context)
    {
        var modelType = context.Metadata.UnderlyingOrModelType;
        if (modelType != typeof(DateTimeOffset) && modelType != typeof(DateTime))
            return null;

        // The attribute set is supplied (and cached) by the framework's model metadata;
        // no reflection happens per request.
        if (context.Metadata is not DefaultModelMetadata metadata)
            return null;

        foreach (var attribute in metadata.Attributes.Attributes)
        {
            switch (attribute)
            {
                case AsUtcAttribute:
                    return new DateTimeOffsetTemporalModelBinder(TemporalInputConversion.AsUtc);
                case FromClientTimezoneAttribute:
                    return new DateTimeOffsetTemporalModelBinder(TemporalInputConversion.FromClientTimezone);
                case FromBusinessTimezoneAttribute:
                    return new DateTimeOffsetTemporalModelBinder(TemporalInputConversion.FromBusinessTimezone);
            }
        }

        return null;
    }
}
