using System.Collections.Immutable;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Models;

/// <summary>
///     Grouped model representing an entity with one or more [Projectable] properties.
///     Used by <see cref="Templates.ProjectableTemplate"/> to generate the nested Expr class.
/// </summary>
internal sealed record ProjectableModel
{
    public string Namespace { get; init; } = "";

    public required string TypeName { get; init; }

    public string FullTypeName => string.IsNullOrEmpty(Namespace)
        ? TypeName
        : $"{Namespace}.{TypeName}";

    public required string Accessibility { get; init; }

    public EquatableArray<ProjectablePropertyModel> Properties { get; init; } =
        EquatableArray<ProjectablePropertyModel>.Empty;

    public bool IsValid => !string.IsNullOrEmpty(TypeName) && Properties.Length > 0;
}
