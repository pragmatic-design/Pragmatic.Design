using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;

namespace Pragmatic.SourceGenerator.Features.Identity.Models;

/// <summary>
///     A role declared with <c>[Role]</c> on a class, as read — before its permissions are resolved through the
///     catalogue and flattened through the roles it includes.
/// </summary>
internal sealed record DeclaredRoleModel
{
    /// <summary>The class's name.</summary>
    public required string TypeName { get; init; }

    /// <summary>The class's namespace; empty for the global one.</summary>
    public required string Namespace { get; init; }

    /// <summary>The class's full name, as a hand-written role's <c>SourceTypeFqn</c> is written — how includes find it.</summary>
    public required string FullName { get; init; }

    public required string Name { get; init; }

    public string? Description { get; init; }

    /// <summary>Whether the members can be generated into it: a top-level <c>partial</c> class.</summary>
    public bool CanBeGenerated { get; init; }

    /// <summary>The <c>[Grants]</c> values the compilation could bind.</summary>
    public EquatableArray<string> Grants { get; init; } = EquatableArray<string>.Empty;

    /// <summary>The <c>[Grants]</c> constant paths it could not — generated in this run, resolved by the catalogue.</summary>
    public EquatableArray<string> GrantPaths { get; init; } = EquatableArray<string>.Empty;

    /// <summary>The roles of this compilation it includes, by full name.</summary>
    public EquatableArray<string> Includes { get; init; } = EquatableArray<string>.Empty;

    /// <summary>The permissions of included <c>[Role]</c> classes of referenced assemblies, read from their metadata.</summary>
    public EquatableArray<string> IncludedFromReferences { get; init; } = EquatableArray<string>.Empty;

    /// <summary>Included hand-written roles of referenced assemblies, whose permissions cannot be read (PRAG1009).</summary>
    public EquatableArray<string> UnreadableIncludes { get; init; } = EquatableArray<string>.Empty;

    public LocationInfo? Location { get; init; }
}
