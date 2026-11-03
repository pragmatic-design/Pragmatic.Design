using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Sql.Models;
using Pragmatic.SourceGen;

namespace Pragmatic.SourceGenerator.Features.Persistence.Sql.Transforms;

/// <summary>
///     A declared query's <c>[ComplexFilter]</c>: the filter object, a <c>[FilterDto&lt;TEntity&gt;]</c>, read as Pragmatic's generator reads it
///     (<c>FilterDtoTransform</c>) and composed as its <c>ToSpecification</c> composes it (<c>FilterDtoApplyTemplate</c>): each <c>[Filter]</c> member
///     that is set is a leaf, each <c>[FilterGroup]</c> member a nested group, the leaves first, joined with AND, or with OR inside a group that says
///     <c>FilterLogic.Or</c>. EF Core runs that generated <c>ToSpecification</c>; here the same leaves are written into the query's shape, where they
///     are compiled like its other filters.
/// </summary>
internal sealed partial class PragmaticQuery
{
    /// <summary>A filter object inside another one more than this deep is taken for a cycle (a DTO that holds itself).</summary>
    private const int MaxFilterDepth = 8;

    private readonly List<QueryFilterGroupModel> _groups = [];

    /// <summary>True when the query composes a <c>[ComplexFilter]</c>.</summary>
    internal bool HasComplexFilter => _groups.Count > 0;

    private void ReadComplexFilter(IPropertySymbol property)
    {
        if (Group(property.Type, $"this.{property.Name}", or: false, depth: 0) is { } group)
            _groups.Add(group);
    }

    /// <summary>The filter object read through <paramref name="access" />; null, with the query refused, when it is not one of the entity's.</summary>
    private QueryFilterGroupModel? Group(ITypeSymbol type, string access, bool or, int depth)
    {
        if (Unwrap(type) is not INamedTypeSymbol dto || !IsFilterDtoOfEntity(dto))
        {
            Unsupported ??= $"[ComplexFilter] reads {access.Substring("this.".Length)}, whose type is not a [FilterDto<{Entity.Name}>]";
            return null;
        }

        if (depth > MaxFilterDepth)
        {
            Unsupported ??= $"the filter object {dto.Name} nests more than {MaxFilterDepth} levels deep";
            return null;
        }

        var conditions = new List<QueryConditionModel>();
        var groups = new List<QueryFilterGroupModel>();
        foreach (var member in dto.GetMembers().OfType<IPropertySymbol>().Where(p => !p.IsIndexer && !p.IsStatic && p.GetMethod is not null))
        {
            var memberAccess = $"{access}!.{member.Name}";
            if (Mark(member, "FilterGroupAttribute") is { } groupMark)
            {
                // FilterLogic: And 0, Or 1.
                var useOr = groupMark.ConstructorArguments.Length > 0 && groupMark.ConstructorArguments[0].Value is 1;
                if (Group(member.Type, memberAccess, useOr, depth + 1) is { } nested)
                    groups.Add(nested);
                continue;
            }

            if (Mark(member, "FilterAttribute") is { } filter && FilterDtoCondition(member, filter, memberAccess) is { } condition)
                conditions.Add(condition);
        }

        return new QueryFilterGroupModel($"{access} is not null", or, conditions.ToEquatableArray(), groups.ToEquatableArray());
    }

    /// <summary>
    ///     A <c>[Filter]</c> member of a filter object, by <c>FilterDtoApplyTemplate</c>'s rules: a string compares with Contains unless the attribute
    ///     names an operator; a string operator on another type, and In on a value that is not a collection, compare for equality; the member is
    ///     applied when it is set.
    /// </summary>
    private QueryConditionModel? FilterDtoCondition(IPropertySymbol member, AttributeData filter, string access)
    {
        var isString = Unwrap(member.Type).SpecialType == SpecialType.System_String;
        var collection = IsScalarCollection(member.Type);
        var operatorKind = Named(filter, "Operator") is int op ? op : isString ? 2 : 0;
        if (operatorKind == 10)
        {
            Unsupported ??= $"Between on {access.Substring("this.".Length).Replace("!", "")} is not compiled yet";
            return null;
        }

        if ((operatorKind is 2 or 3 or 4 && !isString) || (operatorKind == 9 && !collection))
            operatorKind = 0;
        var path = Named(filter, "MapTo") as string ?? member.Name;
        var ignoreCase = Named(filter, "IgnoreCase") is true && isString;
        var nullableValue = member.Type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T;
        var value = nullableValue ? access + ".Value" : member.Type.IsValueType ? access : access + "!";
        var entityPath = $"e.{path}";
        var condition = ignoreCase ? Compare(operatorKind, entityPath + ".ToLower()", value + ".ToLower()") : Compare(operatorKind, entityPath, value);
        if ((ignoreCase || operatorKind is 2 or 3 or 4) && EntityPathNullable(path))
            condition = $"{entityPath} != null && {condition}";
        return new QueryConditionModel(member.Type.IsValueType && !nullableValue ? null : $"{access} is not null", condition);
    }

    private bool IsFilterDtoOfEntity(INamedTypeSymbol type)
        => type.GetAttributes().Any(a => a.AttributeClass is { Name: "FilterDtoAttribute", TypeArguments.Length: 1 } attribute
            && attribute.ContainingNamespace.ToDisplayString() + "." == Attributes
            && SymbolEqualityComparer.Default.Equals(attribute.TypeArguments[0], Entity));
}
