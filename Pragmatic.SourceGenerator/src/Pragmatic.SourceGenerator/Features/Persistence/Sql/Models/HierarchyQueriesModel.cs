using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;

/// <summary>
///     A Pragmatic entity's <c>[GenerateHierarchy]</c> methods on an engine: its trees, the engine's session type and the method that walks the
///     parent keys (<see cref="Keys" />), and the engine's package, for the file's header.
/// </summary>
internal sealed record HierarchyQueriesModel(
    string Hint,
    string Package,
    string? Namespace,
    string EntityName,
    string EntityType,
    string Session,
    string Keys,
    EquatableArray<HierarchyTreeModel> Trees);
