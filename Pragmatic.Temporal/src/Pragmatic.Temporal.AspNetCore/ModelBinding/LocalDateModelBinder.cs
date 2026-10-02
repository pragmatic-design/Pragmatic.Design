using Microsoft.AspNetCore.Mvc.ModelBinding;
using Pragmatic.Temporal.Types;

namespace Pragmatic.Temporal.AspNetCore.ModelBinding;

/// <summary>
///     Model binder for <see cref="LocalDate" />.
/// </summary>
public sealed class LocalDateModelBinder : IModelBinder
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
            if (bindingContext.ModelType == typeof(LocalDate?))
                bindingContext.Result = ModelBindingResult.Success(null);
            return Task.CompletedTask;
        }

        if (LocalDate.TryParse(value, out var result))
        {
            bindingContext.Result = ModelBindingResult.Success(result);
        }
        else
        {
            bindingContext.ModelState.AddModelError(bindingContext.ModelName,
                $"'{value}' is not a valid date format. Expected: yyyy-MM-dd");
            bindingContext.Result = ModelBindingResult.Failed();
        }

        return Task.CompletedTask;
    }
}
