namespace Pragmatic.SourceGenerator.Features.Patch.Models;

/// <summary>Represents a single property to generate in the patch DTO.</summary>
internal sealed record PatchPropertyModel
{
    public string Name { get; init; } = "";

    /// <summary>Fully-qualified type of the property.</summary>
    public string TypeFullName { get; init; } = "";

    public bool IsNullable { get; init; }

    /// <summary>Whether the entity exposes a SetXxx() method (private setter pattern).</summary>
    public bool HasSetMethod { get; init; }

    public bool IsValueType { get; init; }
}
