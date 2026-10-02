namespace Pragmatic.SourceGenerator.Features.Manifest.Models;

/// <summary>
///     Describes a domain action or mutation in the manifest.
/// </summary>
internal sealed record ManifestActionModel
{
    public required string Type { get; init; }
    public required string SimpleName { get; init; }
    public required string Kind { get; init; } // "action", "mutation", "voidAction"
    public string? ReturnType { get; init; }
    public string? EntityType { get; init; }
    public string? Boundary { get; init; }
    public bool IsVoid { get; init; }
}

/// <summary>
///     Describes a permission in the manifest.
/// </summary>
internal sealed record ManifestPermissionModel
{
    public required string Name { get; init; }
    public string? Source { get; init; } // "auto-derived", "explicit"
}

/// <summary>
///     Describes validation rules for a property on a target type.
/// </summary>
internal sealed record ManifestValidationModel
{
    public required string TargetType { get; init; }
    public required string Property { get; init; }
    public required string Rule { get; init; }
    public string? Value { get; init; }
}

/// <summary>
///     Individual validation rule on a property.
/// </summary>
internal sealed record ManifestValidationRuleModel
{
    public required string Rule { get; init; }
    public string? Value { get; init; }
    public string? Min { get; init; }
    public string? Max { get; init; }
}
