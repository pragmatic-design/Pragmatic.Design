using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>The members Pragmatic's generator declares on an entity, declared by this one when it does not: its interfaces, its members in order, its filters.</summary>
internal sealed record EntityShapeModel(
    string Hint,
    string? Namespace,
    string EntityName,
    string EntityType,
    EquatableArray<string> Interfaces,
    EquatableArray<ShapeMemberModel> Members,
    EquatableArray<ShapeFilterModel> Filters);
