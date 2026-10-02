using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Actions.Models;

/// <summary>
///     Model for SG-generated validation metadata on a DomainAction or Mutation.
///     Replaces runtime reflection in ValidationFilter (BRIDGE elimination).
/// </summary>
internal sealed record ActionValidationModel
{
    public required string TypeName { get; init; }
    public required string FullTypeName { get; init; }
    public required string Namespace { get; init; }
    public required string Accessibility { get; init; }

    /// <summary>Whether [NoValidation] is present — skip all validation.</summary>
    public bool HasNoValidation { get; init; }

    /// <summary>Whether sync validation (ISyncValidator) should run.</summary>
    public bool RunSync { get; init; } = true;

    /// <summary>Whether async validation (IAsyncValidator) should run.</summary>
    public bool RunAsync { get; init; }

    /// <summary>
    ///     Property names on the action that implement ISyncValidator.
    ///     Used for nested validation without reflection.
    /// </summary>
    public EquatableArray<NestedValidatorProperty> SyncNestedProperties { get; init; } =
        EquatableArray<NestedValidatorProperty>.Empty;

    /// <summary>
    ///     Non-primitive, non-string, non-value-type public properties eligible
    ///     for async validation via IAsyncValidator&lt;T&gt;.
    /// </summary>
    public EquatableArray<AsyncNestedValidatorProperty> AsyncNestedProperties { get; init; } =
        EquatableArray<AsyncNestedValidatorProperty>.Empty;

    public bool IsValid => !string.IsNullOrEmpty(TypeName);
}

/// <param name="PropertyName">The property to validate through.</param>
/// <param name="PropertyTypeName">The validatable type — the element type for a collection.</param>
/// <param name="IsCollection">The property holds many validatables, not one.</param>
/// <remarks>
///     ⚠️ Without the distinction a collection would not be validated: <c>if (Lines is ISyncValidator)</c>
///     is never true for a <c>List&lt;T&gt;</c> — <c>T</c> is the validator.
/// </remarks>
internal sealed record NestedValidatorProperty(
    string PropertyName, string PropertyTypeName, bool IsCollection = false);

/// <param name="PropertyName">The property to validate through.</param>
/// <param name="PropertyTypeName">
///     The type the validator is resolved for — the <b>element</b> type for a collection.
/// </param>
/// <param name="IsCollection">The property holds many values, each validated on its own.</param>
/// <remarks>
///     ⚠️ For a collection, <c>IAsyncValidator&lt;List&lt;T&gt;&gt;</c> is the validator <b>of the list</b>,
///     which nobody registers: the service would always be null and no element validated — a silence
///     harder to see than the synchronous one, because the code would be there and run, and never find
///     anything.
/// </remarks>
internal sealed record AsyncNestedValidatorProperty(
    string PropertyName, string PropertyTypeName, bool IsCollection = false);
