using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>A collection member of a declared DTO: the entity's collection it reads and the child DTO's own declared projection, a companion statement in the plan.</summary>
internal sealed class ProjectedCollection
{
    public ProjectedCollection(string property, CollectionModel collection, EntityModel child, INamedTypeSymbol childDto, SqlInterceptorGenerator.Projection childProjection, List<string> childJoins)
    {
        Property = property;
        Collection = collection;
        Child = child;
        ChildDto = childDto;
        ChildProjection = childProjection;
        ChildJoins = childJoins;
    }

    public string Property { get; }

    public CollectionModel Collection { get; }

    public EntityModel Child { get; }

    public INamedTypeSymbol ChildDto { get; }

    public string ChildDtoName => ChildDto.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);

    public SqlInterceptorGenerator.Projection ChildProjection { get; }

    /// <summary>The joins the child DTO's columns need (a navigation's column by convention), in the child statement.</summary>
    public List<string> ChildJoins { get; }

    /// <summary>The child's foreign key column, appended to the companion's select list for the loader to group on.</summary>
    public string ForeignKeyColumn => Child.Column(Collection.ForeignKey)?.Column ?? Collection.ForeignKey;

    /// <summary>The child's foreign key columns, one per key column of a composite parent, in its key order.</summary>
    public string[] ForeignKeyColumns => Collection.ForeignKeyColumns(Child);
}

/// <summary>The declared projection: a DTO's settable properties read into columns, aggregates over its collections, and collection members as companion statements.</summary>
internal static partial class SqlInterceptorGenerator
{
    internal sealed partial class Projection
    {
        private static readonly Regex AggregatePath = new(@"^(\w+)\.(Count|Sum|Min|Max|Average)(?:\((\w+)\))?$", RegexOptions.Compiled);

        /// <summary>
        ///     A DTO as a projection, without a lambda: each settable property reads the entity column of the same name, the path its
        ///     <c>[MapProperty("Customer.Name")]</c> gives, or by convention <c>CustomerName</c> → navigation Customer, column Name. A
        ///     <c>List&lt;TChildDto&gt;</c> named after a collection (or <c>[MapProperty("Lines")]</c>) is the collection projected into the
        ///     child DTO, loaded in the same command; <c>LinesCount</c>, <c>[MapProperty("Lines.Count")]</c> or
        ///     <c>[MapProperty("Lines.Sum(Quantity)")]</c> is a correlated subquery. Null with a diagnostic when a property has no source.
        /// </summary>
        public static Projection? TryDeclared(INamedTypeSymbol dto, EntityModel entity, Func<INamedTypeSymbol, EntityModel?> entities, List<string> joins, List<Diagnostic>? diagnostics, Location where, Compilation? compilation = null)
            => TryDeclared(dto, entity, entities, joins, diagnostics, where, new HashSet<INamedTypeSymbol>(SymbolEqualityComparer.Default), compilation);

        /// <summary><paramref name="visiting" /> holds the DTOs above this one: a DTO whose collection is of its own type (a tree) has no fixed statement count.</summary>
        private static Projection? TryDeclared(INamedTypeSymbol dto, EntityModel entity, Func<INamedTypeSymbol, EntityModel?> entities, List<string> joins, List<Diagnostic>? diagnostics, Location where, HashSet<INamedTypeSymbol> visiting, Compilation? compilation)
        {
            void Report(string member, string reason) => diagnostics?.Add(Diagnostic.Create(Diagnostics.UnsupportedExpression, where, $"{dto.Name}.{member}", "declared projection", reason));
            if (!visiting.Add(dto))
            {
                Report(dto.Name, "a DTO cannot contain a collection of its own type: a tree has no fixed number of statements; use [GenerateHierarchy] on the entity");
                return null;
            }

            var columns = new List<string>();
            var assignments = new List<string>();
            var collections = new List<ProjectedCollection>();
            var named = new List<(string Name, string Kind)>(); // the members by name, in the columns' order, for a shaped read
            string? keyProperty = null;
            var keyProperties = new string?[entity.Keys.Count]; // a composite key's DTO properties, by key column
            foreach (var property in dto.GetMembers().OfType<IPropertySymbol>().Where(p => !p.IsStatic && p.SetMethod is not null && p.DeclaredAccessibility == Accessibility.Public))
            {
                if (property.GetAttributes().Any(a => a.AttributeClass?.Name == "NotMappedAttribute"))
                    continue;
                var map = property.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "MapPropertyAttribute");
                var path = map is { ConstructorArguments.Length: > 0 } && map.ConstructorArguments[0].Value is string given ? given : null;
                // A List of child DTOs is a collection; a List of strings, Guids or numbers is an array column, read like the others (AmenityDto.Keywords).
                if (IsListOf(property.Type, out var childDto) && !EntityModel.IsEntity(childDto) && EntityModel.ArrayReader(property.Type, out _) is null)
                {
                    var collection = TryCollection(dto, property, path, childDto!, entity, entities, diagnostics, where, visiting, compilation);
                    if (collection is null)
                        return null;
                    collections.Add(collection);
                    continue;
                }

                var aggregate = TryAggregate(property, path, entity, entities, compilation, out var aggregateReader, out var aggregateProblem);
                if (aggregateProblem is not null)
                {
                    Report(property.Name, aggregateProblem);
                    return null;
                }

                if (aggregate is not null)
                {
                    columns.Add(aggregate);
                    assignments.Add($"{property.Name} = {aggregateReader!.Replace("{r}", "r").Replace("{i}", (columns.Count - 1).ToString()).Replace("{d}", "d")}");
                    named.Add((property.Name, LambdaTranslator.KindOf(property.Type)));
                    continue;
                }

                string? column = null;
                ColumnModel? source = null;
                if (path is not null)
                {
                    var parts = path.Split('.');
                    if (parts.Length == 1)
                        column = (source = entity.Column(parts[0])) is { } own ? own.Select("t") : null;
                    else if (parts.Length == 2 && entity.Column(path) is { Owner: not null } member)
                        column = (source = member).Select("t"); // an owned type's member: a column of this table
                    else if (parts.Length == 2)
                        column = Flattened(entity, entities, joins, parts[0], parts[1], out source);
                }
                else if (entity.Column(property.Name) is { } own)
                {
                    column = own.Select("t");
                    source = own;
                }
                else if (entity.Columns.FirstOrDefault(c => c.Owner is not null && c.Owner + c.Member == property.Name) is { } ownedMember)
                {
                    column = ownedMember.Select("t"); // AddressCity → the owned Address's City, by the same convention as a navigation's column
                    source = ownedMember;
                }
                else
                {
                    foreach (var navigation in entity.Navigations)
                    {
                        if (property.Name.Length > navigation.Property.Length && property.Name.StartsWith(navigation.Property, StringComparison.Ordinal))
                        {
                            column = Flattened(entity, entities, joins, navigation.Property, property.Name.Substring(navigation.Property.Length), out source);
                            if (column is not null)
                                break;
                        }
                    }
                }

                // A converted column is read with its own reader (the DTO's property has the model type); an optional owned type's member as nullable; any other with the property's type.
                var reader = source is not null ? source.ProjectedReader(property.Type) : EntityModel.ReaderFor(property.Type);
                if (column is null || reader is null)
                {
                    Report(property.Name, column is null
                        ? $"no column of {entity.Type.Name} feeds it: name it after a column, after a navigation and its column (CustomerName), or give it [MapProperty(\"Customer.Name\")]"
                        : $"type {property.Type.ToDisplayString()} is not read");
                    return null;
                }

                if (source is not null && entity.Keys.Count == 1 && entity.Keys[0] == source && SymbolEqualityComparer.Default.Equals(EntityModel.Unwrap(property.Type), EntityModel.Unwrap(source.Type)))
                    keyProperty = property.Name;
                if (source is not null && entity.Keys.Count > 1 && entity.Keys.IndexOf(source) is var keyAt and >= 0 && SymbolEqualityComparer.Default.Equals(property.Type, source.Type))
                    keyProperties[keyAt] = property.Name; // a composite key: the loader groups on the tuple of these
                columns.Add(column);
                assignments.Add($"{property.Name} = {reader.Replace("{r}", "r").Replace("{i}", (columns.Count - 1).ToString()).Replace("{d}", "d")}");
                named.Add((property.Name, LambdaTranslator.KindOf(source?.Store ?? property.Type)));
            }

            if (collections.Count > 0 && dto.IsValueType)
            {
                Report(collections[0].Property, "a DTO with a collection is a class: the loader assigns the collections after the rows are read");
                return null;
            }

            if (collections.Count > 0 && entity.Keys.Count > 1)
            {
                if (keyProperties.Any(p => p is null))
                {
                    Report(collections[0].Property, $"a DTO with a collection reads the parent's key: add a settable property for each of {entity.Type.Name}'s key columns ({entity.KeyList}), of the same type (the loader groups the children on them)");
                    return null;
                }

                keyProperty = keyProperties[0];
            }

            if (collections.Count > 0 && keyProperty is null)
            {
                Report(collections[0].Property, $"a DTO with a collection reads the parent's key: add a settable property for {entity.Type.Name}.{entity.KeyProperty} (the loader groups the children on it)");
                return null;
            }

            visiting.Remove(dto);
            var fullName = dto.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            return new Projection(columns, $"new {fullName} {{ {string.Join(", ", assignments)} }}", fullName, isAnonymous: false, collections, keyProperty, dto.IsValueType) { Members = named, KeyProperties = entity.Keys.Count > 1 && collections.Count > 0 ? keyProperties.Select(p => p!).ToList() : null };
        }

        /// <summary>A collection member: the entity's collection of that name (or the path's), whose child DTO carries <c>[MapFrom&lt;Child&gt;]</c> and is read with its own declared projection.</summary>
        private static ProjectedCollection? TryCollection(INamedTypeSymbol dto, IPropertySymbol property, string? path, INamedTypeSymbol childDto, EntityModel entity, Func<INamedTypeSymbol, EntityModel?> entities, List<Diagnostic>? diagnostics, Location where, HashSet<INamedTypeSymbol> visiting, Compilation? compilation)
        {
            void Report(string reason) => diagnostics?.Add(Diagnostic.Create(Diagnostics.UnsupportedExpression, where, $"{dto.Name}.{property.Name}", "declared projection", reason));
            var name = path ?? property.Name;
            var collection = entity.Collection(name);
            if (collection is null)
            {
                Report($"no collection of {entity.Type.Name} is named {name}: name the List<{childDto.Name}> after one, or give it [MapProperty(\"Lines\")]");
                return null;
            }

            var child = entities(collection.Child);
            if (child is null)
            {
                Report($"{collection.Child.Name} is not an [Entity] the generator could read");
                return null;
            }

            var mapFrom = childDto.GetAttributes().FirstOrDefault(a => a.AttributeClass is { Name: "MapFromAttribute", TypeArguments.Length: 1 });
            if (mapFrom is null || !SymbolEqualityComparer.Default.Equals(mapFrom.AttributeClass!.TypeArguments[0], collection.Child))
            {
                Report($"the child DTO {childDto.Name} needs [MapFrom<{collection.Child.Name}>]: its projection is registered for the runtime path too");
                return null;
            }

            if (property.SetMethod!.IsInitOnly)
            {
                Report("the collection is assigned after the rows are read: give it a set accessor, not init");
                return null;
            }

            if (entity.HasCompositeKey && !collection.IsComposite && !collection.IsLink)
            {
                Report($"{entity.Type.Name} has a composite key ({entity.KeyList}): {collection.Child.Name} carries no foreign key per key column");
                return null;
            }

            if (collection.IsLink)
            {
                Report($"{entity.Type.Name}.{name} is a many-to-many side: a DTO collection is loaded through the child's foreign key; Include the collection on the entity instead");
                return null;
            }

            var childJoins = new List<string>();
            var childProjection = TryDeclared(childDto, child, entities, childJoins, diagnostics, where, visiting, compilation);
            return childProjection is null ? null : new ProjectedCollection(property.Name, collection, child, childDto, childProjection, childJoins);
        }

        /// <summary>
        ///     An aggregate over a collection as a correlated subquery: <c>[MapProperty("Lines.Count")]</c>, <c>[MapProperty("Lines.Sum(Quantity)")]</c>
        ///     (Sum, Min, Max of a child column), or by convention <c>LinesCount</c>. Null when the property is something else; a problem when it is an
        ///     aggregate the subset cannot write.
        /// </summary>
        private static string? TryAggregate(IPropertySymbol property, string? path, EntityModel entity, Func<INamedTypeSymbol, EntityModel?> entities, Compilation? compilation, out string? reader, out string? problem)
        {
            reader = null;
            problem = null;
            string collectionName, function, argument = "";
            if (path is not null)
            {
                var match = AggregatePath.Match(path);
                if (!match.Success || entity.Collection(match.Groups[1].Value) is null)
                    return null;
                collectionName = match.Groups[1].Value;
                function = match.Groups[2].Value;
                argument = match.Groups[3].Value;
            }
            else if (property.Name.EndsWith("Count", StringComparison.Ordinal) && property.Name.Length > "Count".Length && entity.Column(property.Name) is null && entity.Collection(property.Name.Substring(0, property.Name.Length - "Count".Length)) is not null)
            {
                collectionName = property.Name.Substring(0, property.Name.Length - "Count".Length);
                function = "Count";
            }
            else
            {
                return null;
            }

            var collection = entity.Collection(collectionName)!;
            var child = entities(collection.Child);
            if (child is null)
            {
                problem = $"{collection.Child.Name} is not an [Entity] the generator could read";
                return null;
            }

            var rules = child.Rules.Count == 0 ? null : ChildRules(child, entities, compilation, out problem);
            if (problem is not null)
                return null;

            if (collection.LinkProblem(entity, child) is { } linkProblem) // a composite key on either side: one column per key column
            {
                problem = linkProblem;
                return null;
            }

            var linkJoin = collection.LinkJoin(child, "c", "c_l"); // a many-to-many side: through its join table
            var predicate = collection.Matches(entity, "t", child, collection.IsLink ? "c_l" : "c"); // every key column of a composite parent
            if (child.RowPredicate is { } fixedPredicate)
                predicate += " AND " + fixedPredicate.Replace("t.\"", "c.\""); // a derived type's rows, a [SoftDelete] child's live ones (the fragment names the root alias)
            if (rules is not null)
                predicate += " AND " + rules; // the child's visibility rules: the rows the aggregate sees are the ones the child's reads see
            string value;
            if (function == "Count")
            {
                if (argument.Length > 0 || property.Type.SpecialType != SpecialType.System_Int32)
                {
                    problem = "a Count over a collection takes no column and fills an int";
                    return null;
                }

                value = "COUNT(*)";
                reader = "global::Pragmatic.Persistence.Sql.Planning.SqlValue.Count({r}, {i}, {d})";
            }
            else
            {
                var column = argument.Length > 0 ? child.Column(argument) : null;
                if (column is null || column.Owner is not null)
                {
                    problem = $"{function} over {collectionName} takes a column of {child.Type.Name} in parentheses: [MapProperty(\"{collectionName}.{function}(Quantity)\")]";
                    return null;
                }

                if (function != "Sum" && !EntityModel.IsNullable(property.Type))
                {
                    problem = $"{function} over an empty collection is NULL: make the property nullable ({EntityModel.Unwrap(property.Type).ToDisplayString()}?)";
                    return null;
                }

                if (function == "Average")
                {
                    var averaged = EntityModel.Unwrap(property.Type).SpecialType;
                    if (averaged is not (SpecialType.System_Double or SpecialType.System_Decimal))
                    {
                        problem = $"an Average over {collectionName} fills a double? or a decimal?";
                        return null;
                    }

                    // SQL Server's AVG over an integer column is an integer: a double's mean of one times 1E0 (a floating literal on SQL Server
                    // and MySQL, whose numeric mean would keep 4 more digits only), a decimal's times 1.0. A numeric column's mean is the
                    // dialect's (SQL Server: 6 digits past the point, truncated; MySQL: the column's plus div_precision_increment).
                    var factor = !EntityModel.IsInteger(column.Store) ? "" : averaged == SpecialType.System_Double ? " * 1E0" : " * 1.0";
                    value = $"AVG(c.\"{column.Column}\"{factor})";
                    var cast = averaged == SpecialType.System_Decimal ? "(decimal?)(decimal)" : "(double?)";
                    reader = $"({{r}}.IsDBNull({{i}}) ? null : {cast}global::Pragmatic.Persistence.Sql.Planning.SqlValue.Average({{r}}, {{i}}, {{d}}))";
                    return $"(SELECT {value} FROM \"{child.Table}\" c{(linkJoin is null ? "" : " " + linkJoin)} WHERE {predicate})";
                }

                value = function == "Sum" ? $"COALESCE(SUM(c.\"{column.Column}\"), 0)" : $"{function.ToUpperInvariant()}(c.\"{column.Column}\")";
                reader = EntityModel.ReaderFor(property.Type);
                if (reader is null)
                {
                    problem = $"type {property.Type.ToDisplayString()} is not read";
                    return null;
                }
            }

            return $"(SELECT {value} FROM \"{child.Table}\" c{(linkJoin is null ? "" : " " + linkJoin)} WHERE {predicate})";
        }

        private static bool IsListOf(ITypeSymbol type, out INamedTypeSymbol? element)
        {
            element = null;
            if (type is not INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } generic || generic.OriginalDefinition.ToDisplayString() != "System.Collections.Generic.List<T>")
                return false;
            element = generic.TypeArguments[0] as INamedTypeSymbol;
            return element is not null;
        }

        private static string? Flattened(EntityModel entity, Func<INamedTypeSymbol, EntityModel?> entities, List<string> joins, string navigationName, string columnName, out ColumnModel? column)
        {
            column = null;
            var navigation = entity.Navigation(navigationName);
            var target = navigation is null ? null : entities(navigation.Target);
            column = target?.Column(columnName);
            if (navigation is null || target is null || column is null)
                return null;
            PlanBuilder.Merge(joins, [entity.Join(navigation, target)]);
            return column.Select(navigation.Alias);
        }
    }
}
