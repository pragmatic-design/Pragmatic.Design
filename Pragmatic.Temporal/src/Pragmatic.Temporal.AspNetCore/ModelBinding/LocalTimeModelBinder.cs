using Microsoft.AspNetCore.Mvc.ModelBinding;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.AspNetCore.ModelBinding;

/// <summary>
///     Model binder for <see cref="LocalTime" />.
/// </summary>
public sealed class LocalTimeModelBinder : IModelBinder
{
    /// <inheritdoc />
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        var valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueProviderResult == ValueProviderResult.None)
            return Task.CompletedTask;

        var value = valueProviderResult.FirstValue;
        if (string.IsNullOrWhiteSpace(value))
        {
            if (bindingContext.ModelType == typeof(LocalTime?))
                bindingContext.Result = ModelBindingResult.Success(null);
            return Task.CompletedTask;
        }

        if (LocalTime.TryParse(value, out var result))
        {
            bindingContext.Result = ModelBindingResult.Success(result);
        }
        else
        {
            bindingContext.ModelState.AddModelError(bindingContext.ModelName,
                $"'{value}' is not a valid time format. Expected: HH:mm:ss");
            bindingContext.Result = ModelBindingResult.Failed();
        }

        return Task.CompletedTask;
    }
}
