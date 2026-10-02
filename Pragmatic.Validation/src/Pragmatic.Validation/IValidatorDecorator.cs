namespace Pragmatic.Validation;

/// <summary>
///     Optional marker for async validator decorators. When a decorator wraps a
///     concrete validator (logging, metrics, caching, ...), the runtime uses
///     <see cref="InnerValidatorType"/> to resolve async-validator bindings
///     against the <em>wrapped</em> type instead of the decorator's own type.
/// </summary>
/// <remarks>
///     Without this indirection, <see cref="CompositeValidator{T}"/> would see
///     the decorator's <see cref="object.GetType"/> and miss the binding
///     generated for the inner concrete validator, silently skipping the validator.
/// </remarks>
public interface IValidatorDecorator
{
    /// <summary>The concrete validator type that this decorator wraps.</summary>
    Type InnerValidatorType { get; }
}
