using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Validation.Models;

/// <summary>
///     Model representing all [AsyncValidate&lt;T&gt;] bindings for a single entity type.
///     Used for generating the IAsyncValidatorBindings&lt;T&gt; implementation class.
/// </summary>
internal sealed record AsyncValidatorBindingsModel : IEquatable<AsyncValidatorBindingsModel>
{
    public required string Namespace { get; init; }
    public required string TypeName { get; init; }
    public required string FullTypeName { get; init; }

    public EquatableArray<AsyncValidatorBindingModel> Bindings { get; init; }
        = EquatableArray<AsyncValidatorBindingModel>.Empty;
}
