using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Validation.Models;

/// <summary>
///     Model representing a [Validator] class for code generation.
/// </summary>
internal sealed record ValidatorModel : IEquatable<ValidatorModel>
{
    public required string Namespace { get; init; }
    public required string ValidatorTypeName { get; init; }
    public required string ValidatorFullName { get; init; }
    public required string Accessibility { get; init; }
    public required ValidatedTypeModel ValidatedType { get; init; }
    public required ServiceLifetimeKind Lifetime { get; init; }
    public required bool ValidatedTypeHasSyncValidation { get; init; }
    public LocationInfo? LocationInfo { get; init; }
    public Location? Location => LocationInfo?.ToLocation();
    public bool MissingValidatorInterface { get; init; }

    /// <summary>
    ///     The assembly that declares the validated type, when that type is a mutation or a domain
    ///     action without <c>[Validate]</c> declared outside this compilation (PRAG0215); otherwise null.
    /// </summary>
    public string? OperationDeclaredIn { get; init; }
}
