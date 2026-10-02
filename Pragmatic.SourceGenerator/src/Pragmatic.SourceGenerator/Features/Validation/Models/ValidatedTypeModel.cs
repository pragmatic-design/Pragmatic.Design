namespace Pragmatic.SourceGenerator.Features.Validation.Models;

/// <summary>
///     Model representing the type being validated.
/// </summary>
internal sealed record ValidatedTypeModel : IEquatable<ValidatedTypeModel>
{
    public required string Namespace { get; init; }
    public required string TypeName { get; init; }
    public required string FullName { get; init; }
    public required bool IsValueType { get; init; }
}
