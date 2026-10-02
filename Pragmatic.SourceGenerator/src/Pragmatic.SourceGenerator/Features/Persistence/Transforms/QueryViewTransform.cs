using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Transforms a [QueryView] class declaration into a QueryViewModel.
/// </summary>
internal static class QueryViewTransform
{
    public static QueryViewModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (context.TargetSymbol is not INamedTypeSymbol typeSymbol)
            return null;

        var syntax = context.TargetNode;
        var isRecord = syntax is RecordDeclarationSyntax;

        // Get [QueryView<TRoot>] attribute
        var attr = context.Attributes.FirstOrDefault();
        if (attr?.AttributeClass is not INamedTypeSymbol { TypeArguments.Length: 1 } attrClass)
            return null;

        var rootEntityType = attrClass.TypeArguments[0];
        var compilation = context.SemanticModel.Compilation;

        var groupByProperties = new List<GroupByModel>();
        var aggregateProperties = new List<AggregatePropertyModel>();
        var sourceProperties = new List<SourcePropertyModel>();
        var unresolvedGroupKeys = new List<UnresolvedGroupKeyModel>();

        // Process properties
        foreach (var property in typeSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            ct.ThrowIfCancellationRequested();

            if (property.DeclaredAccessibility != Accessibility.Public)
                continue;

            ProcessProperty(property, rootEntityType, compilation, groupByProperties, aggregateProperties, sourceProperties);
        }

        // Class-level [GroupBy<TEntity>(Properties = "A,B", Via = "Nav")] — navigation-path grouping.
        //   TEntity     = the entity whose properties form the grouping key
        //   Properties  = comma-separated property names on TEntity to group by
        //   Via         = navigation path on the root entity to reach TEntity (single- or
        //                 dotted multi-segment, e.g. "Customer" or "Order.Customer").
        //                 Empty/absent => the properties live on the root entity itself.
        foreach (var classAttr in typeSymbol.GetAttributes())
        {
            if (classAttr.AttributeClass is { Name: "GroupByAttribute", TypeArguments.Length: 1 } groupByClass)
            {
                ProcessClassGroupBy(classAttr, groupByClass, rootEntityType, typeSymbol, compilation,
                    groupByProperties, unresolvedGroupKeys, ct);
            }
        }

        return new QueryViewModel
        {
            TypeName = typeSymbol.Name,
            Namespace = typeSymbol.ContainingNamespace?.ToDisplayString() ?? "",
            Accessibility = typeSymbol.DeclaredAccessibility.ToString().ToLowerInvariant(),
            IsRecord = isRecord,
            RootEntityTypeFullName = rootEntityType.ToDisplayString(),
            GroupByProperties = groupByProperties,
            AggregateProperties = aggregateProperties,
            SourceProperties = sourceProperties,
            InertJoinCount = typeSymbol.GetAttributes()
                .Count(a => a.AttributeClass is { Name: "JoinAttribute" }),
            UnresolvedGroupKeys = [.. unresolvedGroupKeys]
        };
    }

    private static void ProcessProperty(
        IPropertySymbol property,
        ITypeSymbol rootEntityType,
        Compilation compilation,
        List<GroupByModel> groupByProperties,
        List<AggregatePropertyModel> aggregateProperties,
        List<SourcePropertyModel> sourceProperties)
    {
        foreach (var attr in property.GetAttributes())
        {
            var attrName = attr.AttributeClass?.Name;

            switch (attrName)
            {
                case "SumAttribute":
                    ProcessAggregateAttribute(property, attr, AggregateKind.Sum, compilation, aggregateProperties);
                    return;

                case "CountAttribute":
                    ProcessCountAttribute(property, attr, aggregateProperties);
                    return;

                case "AvgAttribute":
                    ProcessAggregateAttribute(property, attr, AggregateKind.Average, compilation, aggregateProperties);
                    return;

                case "MinAttribute":
                    ProcessAggregateAttribute(property, attr, AggregateKind.Min, compilation, aggregateProperties);
                    return;

                case "MaxAttribute":
                    ProcessAggregateAttribute(property, attr, AggregateKind.Max, compilation, aggregateProperties);
                    return;

                case "FromAttribute":
                    ProcessFromAttribute(property, attr, sourceProperties);
                    return;
            }
        }

        // No attribute - check if property name matches entity property (auto group-by)
        if (QueryViewMembers.Find(rootEntityType, property.Name) is { } member)
        {
            groupByProperties.Add(new GroupByModel
            {
                PropertyName = property.Name,
                PropertyType = property.Type.ToDisplayString(),
                EntityType = rootEntityType.ToDisplayString(),
                EntityProperty = property.Name,
                KeyBody = BodyOf(member.Declared, compilation, "e")
            });
        }
    }

    private static void ProcessClassGroupBy(
        AttributeData attr,
        INamedTypeSymbol groupByClass,
        ITypeSymbol rootEntityType,
        INamedTypeSymbol viewSymbol,
        Compilation compilation,
        List<GroupByModel> groupByProperties,
        List<UnresolvedGroupKeyModel> unresolvedGroupKeys,
        CancellationToken ct)
    {
        var groupedEntityType = groupByClass.TypeArguments[0];
        var via = GetAttributeNamedArg<string>(attr, "Via") ?? "";
        var properties = GetAttributeNamedArg<string>(attr, "Properties") ?? "";
        var location = LocationInfo.From(attr.ApplicationSyntaxReference?.GetSyntax(ct).GetLocation());

        // Resolve the navigation path against the root entity. The path may be single-segment
        // ("Customer") or dotted multi-segment ("Order.Customer"). A path that does not resolve is
        // reported: skipping it would group by fewer keys than declared, and nothing would say so.
        var (navResolved, targetEntityType) = ResolveNavigationPath(rootEntityType, via);
        if (!navResolved)
        {
            unresolvedGroupKeys.Add(new UnresolvedGroupKeyModel
            {
                Named = $"Via '{via}'",
                EntityType = rootEntityType.ToDisplayString(),
                Location = location
            });
            return;
        }

        // When Via is given, the grouped properties live on the entity reached via the path;
        // otherwise they live on the root entity. Prefer the resolved nav target, falling back to
        // the declared TEntity (they should agree, but TEntity is the authoritative declaration).
        var propertyOwner = string.IsNullOrEmpty(via) ? rootEntityType : (targetEntityType ?? groupedEntityType);

        foreach (var rawName in properties.Split(','))
        {
            var propertyName = rawName.Trim();
            if (propertyName.Length == 0)
                continue;

            // The property must exist on the owning entity — declared, inherited, or generated.
            if (QueryViewMembers.Find(propertyOwner, propertyName) is not { } member)
            {
                unresolvedGroupKeys.Add(new UnresolvedGroupKeyModel
                {
                    Named = $"key '{propertyName}'",
                    EntityType = propertyOwner.ToDisplayString(),
                    Location = location
                });
                continue;
            }

            // A view property of the same name already grouped by it: a second member of one name in
            // the anonymous key is CS0833.
            if (groupByProperties.Any(g => g.NavigationPath == via && g.EntityProperty == propertyName))
                continue;

            // Project the key onto a view property only when one of the same name exists; otherwise
            // the key only participates in the GROUP BY clause (its value flows through a [From] prop).
            var hasMatchingViewProperty = viewSymbol.GetMembers()
                .OfType<IPropertySymbol>()
                .Any(p => p.Name == propertyName && p.DeclaredAccessibility == Accessibility.Public);

            groupByProperties.Add(new GroupByModel
            {
                PropertyName = propertyName,
                PropertyType = member.TypeName,
                EntityType = propertyOwner.ToDisplayString(),
                NavigationPath = via,
                EntityProperty = propertyName,
                IsProjected = hasMatchingViewProperty,
                KeyBody = BodyOf(member.Declared, compilation, string.IsNullOrEmpty(via) ? "e" : $"e.{via}")
            });
        }
    }

    /// <summary>
    ///     Resolves a (possibly dotted) navigation path against an entity, returning the type
    ///     reached at the end of the path. An empty path resolves to the entity itself.
    /// </summary>
    private static (bool Resolved, ITypeSymbol? Target) ResolveNavigationPath(ITypeSymbol entity, string path)
    {
        if (string.IsNullOrEmpty(path))
            return (true, entity);

        var current = entity;
        foreach (var rawSegment in path.Split('.'))
        {
            var segment = rawSegment.Trim();
            if (segment.Length == 0)
                continue;

            // A foreign key or a trait column has no type to walk into; only a navigation does.
            if (QueryViewMembers.Find(current, segment)?.Type is not { } navigationType)
                return (false, null);

            current = UnwrapCollectionElement(navigationType);
        }

        return (true, current);
    }

    /// <summary>
    ///     The body of a <c>[Projectable]</c> member over <paramref name="source" />, or null for anything
    ///     else — a column, which the key reads by name.
    /// </summary>
    private static string? BodyOf(IPropertySymbol? declared, Compilation compilation, string source)
        => declared is null ? null : ProjectableBody.Of(declared, compilation, source);

    /// <summary>
    ///     Unwraps a collection navigation (e.g. <c>ICollection&lt;OrderLine&gt;</c>) to its element type,
    ///     so a path segment that crosses a one-to-many continues against the element entity.
    /// </summary>
    private static ITypeSymbol UnwrapCollectionElement(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } generic)
        {
            var openName = generic.OriginalDefinition.ToDisplayString();
            if (openName.StartsWith("System.Collections.Generic.") ||
                openName.StartsWith("System.Collections.Immutable."))
                return generic.TypeArguments[0];
        }

        if (type is IArrayTypeSymbol array)
            return array.ElementType;

        return type;
    }

    private static void ProcessAggregateAttribute(
        IPropertySymbol property,
        AttributeData attr,
        AggregateKind kind,
        Compilation compilation,
        List<AggregatePropertyModel> aggregateProperties)
    {
        if (attr.AttributeClass?.TypeArguments.Length != 1)
            return;

        var entityType = attr.AttributeClass.TypeArguments[0];
        var expression = GetAttributeNamedArg<string>(attr, "Expression") ?? "";

        aggregateProperties.Add(new AggregatePropertyModel
        {
            PropertyName = property.Name,
            PropertyType = property.Type.ToDisplayString(),
            Kind = kind,
            EntityType = entityType.ToDisplayString(),
            Expression = expression,
            RowBody = expression.Length == 0 ? null : AggregateExpression.Over(expression, entityType, compilation)
        });
    }

    private static void ProcessCountAttribute(
        IPropertySymbol property,
        AttributeData attr,
        List<AggregatePropertyModel> aggregateProperties)
    {
        if (attr.AttributeClass?.TypeArguments.Length != 1)
            return;

        var entityType = attr.AttributeClass.TypeArguments[0];
        var whereClause = GetAttributeNamedArg<string>(attr, "Where");

        aggregateProperties.Add(new AggregatePropertyModel
        {
            PropertyName = property.Name,
            PropertyType = property.Type.ToDisplayString(),
            Kind = AggregateKind.Count,
            EntityType = entityType.ToDisplayString(),
            Expression = "*",
            WhereClause = whereClause,
            // The author's line, so PRAG0722 lands on the declaration and not on the generated file.
            Location = LocationInfo.From(property.Locations.Length > 0 ? property.Locations[0] : null)
        });
    }

    private static void ProcessFromAttribute(
        IPropertySymbol property,
        AttributeData attr,
        List<SourcePropertyModel> sourceProperties)
    {
        if (attr.AttributeClass?.TypeArguments.Length != 1)
            return;

        var entityType = attr.AttributeClass.TypeArguments[0];
        var entityProperty = GetAttributeNamedArg<string>(attr, "Property") ?? property.Name;

        sourceProperties.Add(new SourcePropertyModel
        {
            PropertyName = property.Name,
            PropertyType = property.Type.ToDisplayString(),
            EntityType = entityType.ToDisplayString(),
            EntityPropertyPath = entityProperty
        });
    }

    private static T? GetAttributeNamedArg<T>(AttributeData attr, string name)
    {
        var arg = attr.NamedArguments.FirstOrDefault(a => a.Key == name);
        if (arg.Value.Value is T value)
            return value;
        return default;
    }
}
