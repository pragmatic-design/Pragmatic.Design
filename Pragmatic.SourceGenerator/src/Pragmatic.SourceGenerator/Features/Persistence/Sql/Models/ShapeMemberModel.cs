namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     A member of an entity's shape: its kind and name, and what the kind needs (the type, whether it is required, a many-to-many collection's
///     join table and columns, null for any other collection).
/// </summary>
internal sealed record ShapeMemberModel(ShapeMemberKind Kind, string Name, string Type = "", bool Required = false, string? JoinTable = null, string? ThisKey = null, string? OtherKey = null);
