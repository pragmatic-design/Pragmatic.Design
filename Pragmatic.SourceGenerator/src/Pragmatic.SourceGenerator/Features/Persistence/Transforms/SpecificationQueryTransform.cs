using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     Reads a <c>[Query]</c> that sits on a specification and describes the query to derive from it.
/// </summary>
/// <remarks>
///     The attribute's type arguments say what the query answers; the member says what it filters. A
///     member that does not return a <c>Specification&lt;TEntity&gt;</c> is not a specification, and
///     nothing is derived from it — the attribute on a class is the other, older shape and is read
///     elsewhere.
/// </remarks>
internal static class SpecificationQueryTransform
{
    private const string SpecificationMetadataName = "Pragmatic.Specification.Specification`1";


    /// <summary>
    ///     Reads the member, and answers with a model either way.
    /// </summary>
    /// <remarks>
    ///     ⚠️ It answered <c>null</c> six different ways, and the caller could not tell "not the shape I
    ///     handle" from "yours, and malformed". The second shipped as silence: the attribute compiled,
    ///     no query was derived, the build was green. Every path now names a reason, and
    ///     <c>QueryFeature</c> reports it as <c>PRAG0729</c>.
    /// </remarks>
    public static DerivedQueryModel Transform(GeneratorAttributeSyntaxContext context, CancellationToken ct)
    {
        var (returnType, parameters, member) = context.TargetSymbol switch
        {
            IMethodSymbol m => (m.ReturnType, m.Parameters, (ISymbol)m),
            IPropertySymbol p => (p.Type, ImmutableArray<IParameterSymbol>.Empty, p),
            _ => (null, ImmutableArray<IParameterSymbol>.Empty, null)
        };

        if (returnType is null || member is null)
            return Declined(DerivedQueryRejection.NotAMember, context.TargetSymbol);

        if (!member.IsStatic)
            return Declined(DerivedQueryRejection.NotStatic, member);

        var specification = context.SemanticModel.Compilation.GetTypeByMetadataName(SpecificationMetadataName);
        if (specification is null)
            return Declined(DerivedQueryRejection.SpecificationTypeMissing, member);

        if (returnType is not INamedTypeSymbol { IsGenericType: true } named
            || !SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, specification)
            || named.TypeArguments.Length != 1
            || named.TypeArguments[0] is not INamedTypeSymbol entity
            || entity.TypeKind == TypeKind.Error)
            return Declined(DerivedQueryRejection.NotASpecification, member);

        var attribute = context.Attributes.FirstOrDefault();
        if (attribute?.AttributeClass is not { } attributeClass)
            return Declined(DerivedQueryRejection.NoResultType, member);

        // [Query<TEntity, TResult>] answers with the DTO; [Query<TEntity>] with the entity. The entity
        // named in the attribute is not read: the specification already says which entity it filters,
        // and a second answer could disagree with the first.
        var result = attributeClass.TypeArguments.Length == 2
            ? attributeClass.TypeArguments[1] as INamedTypeSymbol
            : entity;

        if (result is null)
            return Declined(DerivedQueryRejection.NoResultType, member);

        var container = member.ContainingType;
        if (container is null || container.IsGenericType)
            return Declined(DerivedQueryRejection.GenericContainer, member);

        var (route, verb) = ReadRoute(member);
        var permissions = ReadPermissions(member, context.SemanticModel.Compilation);

        return new DerivedQueryModel
        {
            Namespace = container.ContainingNamespace is { IsGlobalNamespace: false } ns
                ? ns.ToDisplayString()
                : "",
            TypeName = NamingHelper.AppendSuffix(member.Name, "Query"),
            EntityTypeFullName = entity.ToDisplayString(),
            EntityTypeName = entity.Name,
            ResultTypeFullName = result.ToDisplayString(),
            ResultTypeName = result.Name,
            ContainerFullTypeName = container.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            MemberName = member.Name,
            DeclaredOn = member.Name,
            IsProperty = member is IPropertySymbol,
            Paged = ReadFlag(attribute, "Paged"),
            Single = ReadFlag(attribute, "Single"),
            Inputs = parameters
                .Select(p => new DerivedQueryInput(
                    p.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                    p.Name,
                    PermissionNaming.ToPascalCase(p.Name),
                    p.NullableAnnotation == NullableAnnotation.Annotated))
                .ToImmutableArray(),
            Location = LocationInfo.From(member.Locations.FirstOrDefault()),
            // The route, when the author wrote one beside the rule. Without it the derived query stays a
            // query: executable in-process, publishing nothing.
            Route = route,
            HttpMethod = verb,
            Permissions = permissions.Resolved,
            // A permission written as a generated constant cannot bind while this compilation is being
            // analysed — the constant is written by this same generator. It travels as a path and the
            // endpoint feature resolves it against the catalog, exactly as it does for a hand-written
            // operation. Dropping it here would map the route with no authorization at all.
            UnresolvedPermissionPaths = permissions.Unresolved,
            AllowAnonymous = member.GetAttributes().Any(
                a => a.AttributeClass?.ToDisplayString() == Endpoints.EndpointAttributeNames.AllowAnonymous),
            // The keyed DbContext the generated handler resolves. Read from the entity, which is the only
            // thing that knows: the specification's own container says nothing about a boundary.
            BoundaryFullTypeName = BoundaryOwnershipReader.QualifiedBoundaryOf(entity)
        };
    }

    /// <summary>
    ///     A model that derives nothing and says why.
    /// </summary>
    /// <remarks>
    ///     It carries the member's name and position and nothing else: <c>IsValid</c> is false, so no
    ///     query is generated from it, and <c>QueryFeature</c> has what it needs to report.
    /// </remarks>
    private static DerivedQueryModel Declined(DerivedQueryRejection reason, ISymbol? member) => new()
    {
        Rejection = reason,
        DeclaredOn = member?.Name ?? "",
        Location = LocationInfo.From(member?.Locations.FirstOrDefault()),
        TypeName = "",
        EntityTypeFullName = "",
        EntityTypeName = "",
        ResultTypeFullName = "",
        ResultTypeName = "",
        ContainerFullTypeName = "",
        MemberName = member?.Name ?? "",
        Namespace = "",
        IsProperty = member is IPropertySymbol,
        Paged = false,
        Single = false
    };

    private static bool ReadFlag(AttributeData attribute, string name)
        => attribute.NamedArguments.Any(a => a.Key == name && a.Value.Value is true);

    /// <summary>
    ///     The route and verb the specification declares with <c>[Endpoint]</c>, or nulls.
    /// </summary>
    /// <remarks>
    ///     The leading slash is added here, as the endpoint transform adds it for a class: the two routes
    ///     end up in the same table, and one of them missing a slash is a second path for the same URL.
    /// </remarks>
    private static (string? Route, string? Verb) ReadRoute(ISymbol member)
    {
        var attribute = member.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == AttributeNames.Endpoint);

        if (attribute is null || attribute.ConstructorArguments.Length < 2)
            return (null, null);

        var verb = attribute.ConstructorArguments[0].Value is int value
            ? HttpVerbNames.FromValue(value)
            : "Get";

        var route = attribute.ConstructorArguments[1].Value?.ToString();
        if (string.IsNullOrEmpty(route))
            return (null, null);

        return (route![0] == '/' ? route : "/" + route, verb);
    }

    /// <summary>
    ///     The permissions the specification requires: the values that bound, and the constant paths
    ///     that did not.
    /// </summary>
    /// <remarks>
    ///     Split by <c>ActionTransform.ExtractPermissionStrings</c>, the same reader the endpoint and
    ///     action transforms use. A second reader here would be a second answer to "what does this
    ///     attribute say", and the one that drifted would be the one nobody looked at.
    /// </remarks>
    private static (ImmutableArray<string> Resolved, ImmutableArray<string> Unresolved) ReadPermissions(
        ISymbol member, Compilation compilation)
    {
        var resolved = ImmutableArray.CreateBuilder<string>();
        var unresolved = ImmutableArray.CreateBuilder<string>();

        foreach (var attribute in member.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != Endpoints.EndpointAttributeNames.RequirePermission)
                continue;

            var (bound, paths) = Actions.Transforms.ActionTransform.ExtractPermissionStrings(attribute, compilation);
            resolved.AddRange(bound.Where(static s => !string.IsNullOrEmpty(s)));
            unresolved.AddRange(paths);
        }

        return (resolved.ToImmutable(), unresolved.ToImmutable());
    }
}
