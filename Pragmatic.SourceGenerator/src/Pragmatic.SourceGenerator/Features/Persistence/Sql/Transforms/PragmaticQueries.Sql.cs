using Pragmatic.Persistence.Sql;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Transforms;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>A declared query's Sql half: its sorts as an ordering of compiled keys (<c>ToSqlOrdering()</c>), the text of each key per dialect.</summary>
internal sealed partial class PragmaticQuery
{
    /// <summary>The query's shape on the Sql engine.</summary>
    public (string Hint, string Source) Shape() => ShapeText("Pragmatic.Persistence.Sql", "Sql engine", "SqlQueryShape", SqlOrdering());

    private QueryOrderingModel SqlOrdering()
        => new(
            "ToSqlOrdering", $"global::Pragmatic.Persistence.Sql.Planning.SqlOrdering<{EntityName}>", "global::Pragmatic.Persistence.Sql.Planning.SqlOrderKey",
            Sorts.OrderBy(s => s.Priority).Select(s => new QuerySortModel(s.Name, s.Column.Replace('.', '_'), s.Direction)).ToEquatableArray(),
            Sorts.Select(s => s.Column).Append("PersistenceId").Distinct().Select(c => new OrderKeyModel(c.Replace('.', '_'), OrderKeyDialects(ColumnOf(Entity, c)).ToEquatableArray())).ToEquatableArray());

    /// <summary>The texts of an order key on the column <paramref name="column" /> of alias t, one per dialect.</summary>
    internal static IEnumerable<string> OrderKeyDialects(string column)
        => new[] { SqlDialect.Sqlite, SqlDialect.PostgreSql, SqlDialect.SqlServer, SqlDialect.MySql, SqlDialect.Oracle }.Select(d => PlanBuilder.ForDialectText($"t.\"{column}\"", d));

    /// <summary>
    ///     The column of <paramref name="entity" />'s table a sort's member path orders by, named as the Sql engine's model names it (#564): the
    ///     generated <c>Id</c> alias is <c>PersistenceId</c>, a <c>[Column("…")]</c> names its own, an owned type's member is <c>Owner_Member</c>.
    ///     A member the entity's own declaration does not show (one the shape adds, as a trait's stamps) keeps its name, which is its column.
    /// </summary>
    internal static string ColumnOf(INamedTypeSymbol entity, string path)
    {
        var parts = path.Split('.');
        var property = Member(entity, parts[0]);
        // The shape's Id is `Id => PersistenceId`: absent where the query is read without the shape, read-only where a shape file declares the
        // query (a [Resource]'s search). A hand-declared key with a setter or [Key] is a column of its own.
        if (path == "Id" && (property is null ? QueryTransform.IdIsTheGeneratedAlias(entity) : property.SetMethod is null && !property.GetAttributes().Any(a => a.AttributeClass?.Name == "KeyAttribute")))
            return "PersistenceId";
        if (parts.Length == 1)
            return property is null ? path : Named(property);
        if (parts.Length == 2 && property?.Type is INamedTypeSymbol owned && !EntityModel.IsEntity(owned) && Member(owned, parts[1]) is { } member)
            return parts[0] + "_" + Named(member);
        return path;
    }

    private static IPropertySymbol? Member(INamedTypeSymbol type, string name)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType)
        {
            if (current.GetMembers(name).OfType<IPropertySymbol>().FirstOrDefault() is { } property)
                return property;
        }

        return null;
    }

    private static string Named(IPropertySymbol property)
        => property.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "ColumnAttribute") is { ConstructorArguments.Length: > 0 } column && column.ConstructorArguments[0].Value is string name
            ? name
            : property.Name;
}
