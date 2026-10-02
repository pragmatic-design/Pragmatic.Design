using System.Collections.Immutable;
using System.Linq;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;
using Pragmatic.SourceGenerator.Features.Persistence.Models;

namespace Pragmatic.SourceGenerator.Features.Persistence.Transforms;

/// <summary>
///     The route of a query derived from a specification, as an endpoint model.
/// </summary>
/// <remarks>
///     <para>
///         The derived type is written by this generator and never exists as a symbol while the endpoint
///         feature runs, so it travels as a model — the same channel <c>[Resource]</c> and the traits
///         already use for the operations they scaffold.
///     </para>
///     <para>
///         Everything here comes from the specification's own declaration or from the entity. Nothing is
///         invented: a rule that declares no route produces no model, which is why promotion to a route
///         is opt-in on top of promotion to a query.
///     </para>
/// </remarks>
internal static class DerivedQueryEndpointBuilder
{
    /// <summary>The endpoint models for the derived queries that declare a route.</summary>
    public static ImmutableArray<EndpointModel> Build(ImmutableArray<DerivedQueryModel> queries)
    {
        if (queries.IsDefaultOrEmpty)
            return ImmutableArray<EndpointModel>.Empty;

        var builder = ImmutableArray.CreateBuilder<EndpointModel>();

        foreach (var query in queries)
        {
            if (!query.IsValid || !query.HasRoute)
                continue;

            builder.Add(ModelFor(query));
        }

        return builder.ToImmutable();
    }

    private static EndpointModel ModelFor(DerivedQueryModel query)
    {
        var fullTypeName = string.IsNullOrEmpty(query.Namespace)
            ? $"global::{query.TypeName}"
            : $"global::{query.Namespace}.{query.TypeName}";

        var result = $"global::{query.ResultTypeFullName}";

        return new EndpointModel
        {
            Namespace = query.Namespace,
            TypeName = query.TypeName,
            FullTypeName = fullTypeName,
            Accessibility = "public",
            HttpMethod = query.HttpMethod ?? "Get",
            Route = query.Route!,
            IsVoid = false,
            IsDomainAction = false,
            IsQuery = true,
            QueryEntityType = $"global::{query.EntityTypeFullName}",
            QueryResultType = result,
            // What the route puts on the wire: one of them for a Single query, a page of them for a paged
            // one, a list otherwise. The three shapes are what the query handler already renders; naming a
            // fourth here would publish a contract the handler does not produce.
            ResponseType = query.Single
                ? result
                : query.Paged
                    ? $"global::Pragmatic.Persistence.Query.Results.PagedResult<{result}>"
                    : $"global::System.Collections.Generic.IReadOnlyList<{result}>",
            QueryBoundaryType = query.BoundaryFullTypeName,
            QueryIsPaged = query.Paged,
            QueryIsSingle = query.Single,
            QueryParameters = Parameters(query),
            Authorization = Authorization(query),
            Summary = $"{query.MemberName} — derived from a specification",
            HintPrefix = $"_Specification.{query.EntityTypeName}",
            HintSuffix = query.TypeName,
        };
    }

    /// <summary>
    ///     The specification's parameters, bound from the query string.
    /// </summary>
    /// <remarks>
    ///     Read from the same inputs the derived type declares as properties, so the route cannot bind a
    ///     different set from the one the query filters on — the defect the scaffolded search had, where
    ///     the type declared filters and the endpoint bound none of them.
    /// </remarks>
    private static EquatableArray<QueryParameterModel> Parameters(DerivedQueryModel query)
    {
        if (query.Inputs.Length == 0)
            return EquatableArray<QueryParameterModel>.Empty;

        var builder = ImmutableArray.CreateBuilder<QueryParameterModel>();

        foreach (var input in query.Inputs)
        {
            builder.Add(new QueryParameterModel
            {
                Name = input.ParameterName,
                PropertyName = input.PropertyName,
                TypeName = input.TypeName,
                // A specification's parameter has no default: the rule needs it to mean anything, and a
                // request that omits it has not asked the question the rule answers.
                IsRequired = !input.IsNullable,
                // IsValueType is left alone: the query handler does not read it — it is the
                // action/mutation handlers that unwrap a nullable with it — and guessing it from a type
                // name would put a wrong answer where someone will one day read a right one.
                BindKind = BindKindNames.FromTypeName(input.TypeName),
            });
        }

        return new EquatableArray<QueryParameterModel>(builder.ToImmutable());
    }

    private static AuthorizationModel? Authorization(DerivedQueryModel query)
    {
        if (query.AllowAnonymous)
            return new AuthorizationModel { AllowAnonymous = true };

        if (query.Permissions.Length == 0 && query.UnresolvedPermissionPaths.Length == 0)
            return null;

        return new AuthorizationModel
        {
            IsRequired = true,
            RequiredPermissions = new EquatableArray<string>(query.Permissions.AsImmutableArray()),
            UnresolvedRequiredPermissionPaths =
                new EquatableArray<string>(query.UnresolvedPermissionPaths.AsImmutableArray()),
        };
    }
}
