using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Pragmatic.Temporal.Context;

namespace Pragmatic.Temporal.AspNetCore.ModelBinding;

/// <summary>
///     Binds <see cref="DateTimeOffset" /> and <see cref="DateTime" /> values (and their
///     nullable forms) applying the timezone conversion attribute found on the parameter
///     or property: values that carry an explicit offset are normalized to UTC; values
///     without offset information are interpreted per the attribute — as UTC, or as wall
///     time in the client/business timezone of the current <see cref="TemporalContext" />
///     (DST-safe via the context's configured policies).
/// </summary>
public sealed class DateTimeOffsetTemporalModelBinder : IModelBinder
{
    private readonly TemporalInputConversion _conversion;

    /// <summary>Creates a binder applying the given input conversion.</summary>
    public DateTimeOffsetTemporalModelBinder(TemporalInputConversion conversion)
    {
        _conversion = conversion;
    }

    /// <inheritdoc />
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueProviderResult == ValueProviderResult.None)
            return Task.CompletedTask;

        var value = valueProviderResult.FirstValue;
        var modelType = bindingContext.ModelType;

        if (string.IsNullOrWhiteSpace(value))
        {
            if (modelType == typeof(DateTimeOffset?) || modelType == typeof(DateTime?))
                bindingContext.Result = ModelBindingResult.Success(null);
            return Task.CompletedTask;
        }

        if (!TryConvert(value, bindingContext, out var instant))
        {
            bindingContext.ModelState.AddModelError(bindingContext.ModelName,
                $"'{value}' is not a valid date/time format. Expected ISO 8601, e.g. 2026-01-15T10:30:00 or 2026-01-15T10:30:00+01:00");
            bindingContext.Result = ModelBindingResult.Failed();
            return Task.CompletedTask;
        }

        // No ternary here: the implicit DateTime→DateTimeOffset conversion would make
        // the common type DateTimeOffset and box the wrong CLR type for DateTime models.
        object model;
        if (modelType == typeof(DateTime) || modelType == typeof(DateTime?))
            model = instant.UtcDateTime;
        else
            model = instant;

        bindingContext.Result = ModelBindingResult.Success(model);
        return Task.CompletedTask;
    }

    private bool TryConvert(string value, ModelBindingContext bindingContext, out DateTimeOffset instant)
    {
        instant = default;

        // RoundtripKind keeps the offset information intact: Kind stays Unspecified
        // when (and only when) the string carried no offset/Z designator.
        if (!DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed))
            return false;

        if (parsed.Kind != DateTimeKind.Unspecified)
        {
            // The string carried explicit offset information — the instant is known.
            // Re-parse as DateTimeOffset to preserve the exact original offset.
            if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var withOffset))
                return false;

            instant = withOffset.ToUniversalTime();
            return true;
        }

        switch (_conversion)
        {
            case TemporalInputConversion.AsUtc:
                instant = new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Utc));
                return true;

            case TemporalInputConversion.FromClientTimezone:
            case TemporalInputConversion.FromBusinessTimezone:
            {
                var context = bindingContext.HttpContext.RequestServices.GetRequiredService<TemporalContext>();
                instant = _conversion == TemporalInputConversion.FromClientTimezone
                    ? context.ClientToUtc(parsed)
                    : context.BusinessToUtc(parsed);
                return true;
            }

            default:
                return false;
        }
    }
}
