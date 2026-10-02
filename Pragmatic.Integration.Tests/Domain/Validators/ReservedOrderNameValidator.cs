using Pragmatic.Integration.Tests.Domain.Actions;
using Pragmatic.Validation;
using Pragmatic.Validation.Attributes;
using Pragmatic.Validation.Types;

namespace Pragmatic.Integration.Tests.Domain.Validators;

/// <summary>
///     An async rule for an action that does not carry <c>[Validate]</c>: declaring the validator is
///     the whole opt-in, for an action as for a mutation.
/// </summary>
[Validator]
public sealed class ReservedOrderNameValidator : IAsyncValidator<CreateOrderAction>
{
    public const string Reserved = "reserved";

    public Task<ValidationError> ValidateAsync(CreateOrderAction instance, CancellationToken ct = default)
        => Task.FromResult(instance.Name == Reserved
            ? ValidationError.For(nameof(CreateOrderAction.Name), "validation.order.name_reserved")
            : ValidationError.Valid);
}
