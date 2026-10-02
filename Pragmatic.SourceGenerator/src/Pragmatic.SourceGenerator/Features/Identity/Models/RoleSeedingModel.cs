using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Identity.Models;

/// <summary>
///     A role definition parsed from roles.pragmatic.json.
/// </summary>
internal sealed record RoleSeedModel
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public EquatableArray<string> Permissions { get; init; } = EquatableArray<string>.Empty;
    public EquatableArray<string> Inherits { get; init; } = EquatableArray<string>.Empty;
}

/// <summary>
///     A group definition parsed from roles.pragmatic.json.
/// </summary>
internal sealed record GroupSeedModel
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public EquatableArray<string> Roles { get; init; } = EquatableArray<string>.Empty;
}

/// <summary>
///     Aggregated seeding model from roles.pragmatic.json.
/// </summary>
internal sealed record RoleSeedingAggregateModel
{
    public EquatableArray<RoleSeedModel> Roles { get; init; } = EquatableArray<RoleSeedModel>.Empty;
    public EquatableArray<GroupSeedModel> Groups { get; init; } = EquatableArray<GroupSeedModel>.Empty;
    public required string Namespace { get; init; }
}
