using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;
using Pragmatic.SourceGenerator.Features.Resource.Models;

namespace Pragmatic.SourceGenerator.Features.Resource.Transforms;

/// <summary>
///     Reads the partial declarations a developer writes to decorate an operation <c>[Resource]</c>
///     scaffolds.
/// </summary>
/// <remarks>
///     <para>
///         Attributes on a partial class are <b>combined</b> across its parts — that is the C# rule,
///         not a mechanism of ours. So a developer writes
///         <c>[RequirePermission(…)] public partial class ResourceCreateGuestMutation;</c> in a file of
///         their own and the type ends up carrying it. What does not happen by itself is the
///         <i>generator</i> seeing it: the scaffolded models are built programmatically from the entity,
///         and nothing looks at the developer's part. This does.
///     </para>
///     <para>
///         Matching is by <b>type identity</b> — namespace and name — not by a naming heuristic. Which
///         is also why a typo has to be reported: a declaration that matches nothing is an empty class
///         and a default that silently stays in force. That is PRAG2606.
///     </para>
/// </remarks>
internal static class ResourceOverrideTransform
{
    /// <summary>
    ///     A partial declaration carrying at least one attribute that overrides a scaffolded operation.
    /// </summary>
    internal static ResourceOverrideModel? Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (context.TargetSymbol is not INamedTypeSymbol symbol)
            return null;

        if (context.TargetNode is not TypeDeclarationSyntax declaration)
            return null;

        if (!declaration.Modifiers.Any(m => m.IsKind(Microsoft.CodeAnalysis.CSharp.SyntaxKind.PartialKeyword)))
            return null;

        // Attributes, or attributes plus filters — nothing else. A part that declares anything more is
        // the developer writing their own type, and naming it like a scaffolded one is their business:
        // the compiler will tell them if it collides.
        var filters = ParseDeclaredFilters(declaration, context.SemanticModel);
        if (declaration.Members.Count != filters.Length)
            return null;

        var ns = symbol.ContainingNamespace.IsGlobalNamespace
            ? ""
            : symbol.ContainingNamespace.ToDisplayString();

        var (all, any, unresolvedAll, unresolvedAny) =
            Actions.Transforms.ActionTransform.ParseRequirePermissions(symbol, context.SemanticModel.Compilation);

        var key = string.IsNullOrEmpty(ns) ? symbol.Name : $"{ns}.{symbol.Name}";

        return new ResourceOverrideModel
        {
            Namespace = ns,
            TypeName = symbol.Name,
            RequireAllPermissions = all,
            RequireAnyPermissions = any,
            UnresolvedRequireAllPaths = unresolvedAll,
            UnresolvedRequireAnyPaths = unresolvedAny,
            AllowAnonymous = Actions.Transforms.ActionTransform.ParseAllowAnonymous(symbol),
            PolicyTypeFullName = Actions.Transforms.ActionTransform.ParseRequirePolicy(symbol),
            DeclaredDto = ParseReturnsDto(symbol, key),
            DeclaredFilters = filters,
            LocationInfo = LocationInfo.From(declaration.Identifier.GetLocation()),
        };
    }

    /// <summary>
    ///     The filters a developer declared as properties on their own part of a scaffolded query.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Declared the way a hand-written query declares them — a property carrying
    ///         <c>[Filter]</c> — and read by the same transform, so <c>Operator</c>, <c>MapTo</c>,
    ///         <c>IgnoreCase</c> and the string-defaults-to-Contains rule mean here exactly what they
    ///         mean there. An attribute listing property names would have been a second vocabulary for
    ///         something that already has one, and a typo in it would produce nothing rather than an
    ///         error.
    ///     </para>
    ///     <para>
    ///         Read from the <b>syntax</b> of this part rather than from the type's members: the symbol
    ///         carries every part's properties, including the ones this generator is emitting, so
    ///         asking it would hand back the convention filters as if the developer had written them.
    ///     </para>
    /// </remarks>
    private static EquatableArray<Persistence.Models.QueryPropertyModel> ParseDeclaredFilters(
        TypeDeclarationSyntax declaration, SemanticModel semanticModel)
    {
        if (declaration.Members.Count == 0)
            return EquatableArray<Persistence.Models.QueryPropertyModel>.Empty;

        var filters = ImmutableArray.CreateBuilder<Persistence.Models.QueryPropertyModel>();
        foreach (var member in declaration.Members)
        {
            if (member is not PropertyDeclarationSyntax property)
                continue;

            if (semanticModel.GetDeclaredSymbol(property) is not IPropertySymbol symbol)
                continue;

            // Only a property that says it is one. QueryTransform also treats a nullable property with
            // no attribute as a filter by convention, which is right on a query type and wrong here:
            // it would make any hand-written class carrying [RequirePermission] look like a decoration
            // of a scaffolded operation, and PRAG2607 would report it as a mistyped one.
            if (!DeclaresFilterOrSort(symbol))
                continue;

            if (Persistence.Transforms.QueryTransform.CreatePropertyModel(symbol) is { } model)
                filters.Add(model);
        }

        return filters.ToImmutable();
    }

    /// <summary>Whether the property carries <c>[Filter]</c> or <c>[Sort]</c> explicitly.</summary>
    private static bool DeclaresFilterOrSort(IPropertySymbol property)
    {
        foreach (var attribute in property.GetAttributes())
        {
            if (attribute.AttributeClass is { Name: "FilterAttribute" or "SortAttribute" } declaration
                && declaration.ContainingNamespace?.ToDisplayString()
                    == "Pragmatic.Persistence.Query.Attributes")
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Reads <c>[ReturnsDto&lt;T&gt;]</c> and, on the type it names, whether a projection will exist.
    /// </summary>
    /// <remarks>
    ///     The projection is the part that cannot be left to runtime. A read query projects in the
    ///     database, and the executor falls back to <c>OfType&lt;TResult&gt;()</c> when the DTO has none
    ///     — which matches nothing and answers an empty result rather than an error. So it is checked
    ///     here, where the developer's type is a symbol and the attributes on it can be read, and
    ///     reported as PRAG2608 before the build finishes.
    ///     <para>
    ///         The check is for the two attributes, not for a <c>Projection</c> member: that member is
    ///         itself generated, so it does not exist in the compilation this runs against.
    ///     </para>
    /// </remarks>
    private static ResourceDeclaredDto? ParseReturnsDto(INamedTypeSymbol symbol, string operationKey)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            // Matched on name plus namespace, never on ToDisplayString(): a generic attribute displays
            // with its type argument, so the comparison would never hold and the branch would never run.
            // List patterns are out: they lower to System.Index, which netstandard2.0 does not have.
            if (attribute.AttributeClass is not { Name: "ReturnsDtoAttribute" } declaration
                || declaration.ContainingNamespace?.ToDisplayString() != "Pragmatic.Persistence.Entity"
                || declaration.TypeArguments.Length != 1
                || declaration.TypeArguments[0] is not INamedTypeSymbol dto)
                continue;

            var hasMapFrom = false;
            var hasProjection = false;
            string? mapsFrom = null;

            foreach (var dtoAttribute in dto.GetAttributes())
            {
                if (dtoAttribute.AttributeClass is not { } mapping
                    || mapping.ContainingNamespace?.ToDisplayString() != "Pragmatic.Mapping.Attributes")
                    continue;

                switch (mapping.Name)
                {
                    case "MapFromAttribute" when mapping.TypeArguments.Length == 1:
                        hasMapFrom = true;
                        mapsFrom = mapping.TypeArguments[0].ToDisplayString();
                        break;

                    case "GenerateProjectionAttribute":
                        hasProjection = true;
                        break;
                }
            }

            return new ResourceDeclaredDto
            {
                OperationKey = operationKey,
                DtoFullTypeName = dto.ToDisplayString(),
                DtoTypeName = dto.Name,
                HasProjection = hasMapFrom && hasProjection,
                MapsFromEntity = mapsFrom,
            };
        }

        return null;
    }

    /// <summary>
    ///     Replaces a scaffolded endpoint's authorization with what the developer declared, if they did.
    /// </summary>
    /// <remarks>
    ///     Replaces rather than merges, and that is the whole contract: a default you can only tighten
    ///     is not a default you can change. <c>[AllowAnonymous]</c> is therefore able to open an
    ///     operation the scaffolding closed, which is the point of writing it.
    /// </remarks>
    public static EndpointModel Apply(
        EndpointModel endpoint, ImmutableDictionary<string, ResourceOverrideModel> index)
    {
        var key = string.IsNullOrEmpty(endpoint.Namespace)
            ? endpoint.TypeName
            : $"{endpoint.Namespace}.{endpoint.TypeName}";

        if (!index.TryGetValue(key, out var declared) || !declared.HasAuthorization)
            return endpoint;

        return endpoint with
        {
            Authorization = new AuthorizationModel
            {
                IsRequired = !declared.AllowAnonymous,
                AllowAnonymous = declared.AllowAnonymous,
                RequiredPermissions = declared.RequireAllPermissions,
                AnyPermissions = declared.RequireAnyPermissions,
                UnresolvedRequiredPermissionPaths = declared.UnresolvedRequireAllPaths,
                UnresolvedAnyPermissionPaths = declared.UnresolvedRequireAnyPaths,
                PolicyName = declared.PolicyTypeFullName,
            },
        };
    }

    /// <summary>The type names a set of scaffolded endpoints occupies, for the PRAG2606 message.</summary>
    public static ImmutableHashSet<string> KeysOf(ImmutableArray<EndpointModel> endpoints)
    {
        if (endpoints.IsDefaultOrEmpty)
            return ImmutableHashSet<string>.Empty;

        var builder = ImmutableHashSet.CreateBuilder<string>(StringComparer.Ordinal);
        foreach (var endpoint in endpoints)
        {
            builder.Add(string.IsNullOrEmpty(endpoint.Namespace)
                ? endpoint.TypeName
                : $"{endpoint.Namespace}.{endpoint.TypeName}");
        }

        return builder.ToImmutable();
    }

    /// <summary>
    ///     Indexes the overrides by the type they decorate, so a builder can look one up in O(1).
    /// </summary>
    public static ImmutableDictionary<string, ResourceOverrideModel> Index(
        ImmutableArray<ResourceOverrideModel> overrides)
    {
        if (overrides.IsDefaultOrEmpty)
            return ImmutableDictionary<string, ResourceOverrideModel>.Empty;

        var builder = ImmutableDictionary.CreateBuilder<string, ResourceOverrideModel>(StringComparer.Ordinal);
        foreach (var model in overrides)
            builder[model.Key] = model;

        return builder.ToImmutable();
    }
}
