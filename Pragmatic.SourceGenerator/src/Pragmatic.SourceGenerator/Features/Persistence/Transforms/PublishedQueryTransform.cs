using System;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Reads a <c>[Published]</c> query: pulls the entity/result from its <c>[Query&lt;E, Dto&gt;]</c>, resolves
///     the entity's id type from <c>[Entity]</c>, and derives the read-contract name/namespace (#3).
/// </summary>
internal static class PublishedQueryTransform
{
    private const string QueryAttributeMetadataName = "QueryAttribute";
    private const string QueryAttributeNamespace = "Pragmatic.Persistence.Query.Attributes";
    private const string EntityAttributeName = "EntityAttribute";

    public static PublishedQueryModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        if (context.TargetSymbol is not INamedTypeSymbol queryType)
            return null;

        // The [Query<E, Dto>] (or [Query<E>]) the query class also carries.
        var queryAttr = queryType.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass is { } ac &&
            ac.Name == QueryAttributeMetadataName &&
            ac.ContainingNamespace?.ToDisplayString() == QueryAttributeNamespace);

        if (queryAttr?.AttributeClass is not { TypeArguments.Length: >= 1 } qa)
            return null;

        var entityType = qa.TypeArguments[0] as INamedTypeSymbol;
        var resultType = (qa.TypeArguments.Length == 2 ? qa.TypeArguments[1] : qa.TypeArguments[0]) as INamedTypeSymbol;
        if (entityType is null || resultType is null)
            return null;

        var publishedAttr = context.Attributes.FirstOrDefault();
        var contractName = ReadName(publishedAttr, "ContractName");
        var methodName = ReadName(publishedAttr, "MethodName") ?? DefaultMethodName(queryType.Name);
        var ns = queryType.ContainingNamespace.IsGlobalNamespace ? "" : queryType.ContainingNamespace.ToDisplayString();
        var module = DeriveModule(ns);

        return new PublishedQueryModel
        {
            ContractName = contractName ?? $"I{module}Reads",
            ContractNamespace = DeriveContractNamespace(ns),
            MethodName = methodName,
            QueryTypeShortName = queryType.Name,
            Location = Core.LocationInfo.From(queryType.Locations.FirstOrDefault()),
            QueryTypeFullName = queryType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            EntityTypeFullName = entityType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            EntityIdTypeFullName = ResolveEntityIdType(entityType),
            EntityShortName = entityType.Name,
            ResultTypeFullName = resultType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            Strategy = QueryStrategyParser.Read(queryType)
        };
    }

    private static string? ReadName(AttributeData? publishedAttr, string argument)
    {
        var value = publishedAttr?.NamedArguments
            .FirstOrDefault(n => n.Key == argument).Value.Value as string;
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>The class name with a trailing <c>Query</c> removed, when something is left.</summary>
    /// <remarks>
    ///     ⚠️ The default was the class name whole, so the only surface this feature has read
    ///     <c>SearchCategoriesQuery(new SearchCategoriesQuery { … })</c>. The suffix is dropped only
    ///     when the remainder is non-empty: a method with no name is not a name an author can be told
    ///     to fix. Two classes that strip to the same name are <c>PRAG0728</c>, not a duplicate member.
    /// </remarks>
    private static string DefaultMethodName(string typeName)
    {
        const string suffix = "Query";
        return typeName.Length > suffix.Length && typeName.EndsWith(suffix, StringComparison.Ordinal)
            ? typeName.Substring(0, typeName.Length - suffix.Length)
            : typeName;
    }

    /// <summary>Resolves the entity's id type from its <c>[Entity]</c> (generic arg or ctor arg), default Guid.</summary>
    private static string ResolveEntityIdType(INamedTypeSymbol entityType)
    {
        var entityAttr = entityType.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == EntityAttributeName);
        if (entityAttr?.AttributeClass is { IsGenericType: true } ac && ac.TypeArguments.Length > 0)
            return ac.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (entityAttr is { ConstructorArguments.Length: > 0 } &&
            entityAttr.ConstructorArguments[0].Value is INamedTypeSymbol idSymbol)
            return idSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        return "global::System.Guid";
    }

    /// <summary>Module = second namespace segment (App.{Module}.…), falling back to the first/last segment.</summary>
    private static string DeriveModule(string ns)
    {
        if (string.IsNullOrEmpty(ns))
            return "App";
        var segments = ns.Split('.');
        return segments.Length >= 2 ? segments[1] : segments[0];
    }

    private static string DeriveContractNamespace(string ns)
    {
        if (string.IsNullOrEmpty(ns))
            return "Contracts";
        var segments = ns.Split('.');
        var root = segments.Length >= 2 ? $"{segments[0]}.{segments[1]}" : segments[0];
        return $"{root}.Contracts";
    }
}
