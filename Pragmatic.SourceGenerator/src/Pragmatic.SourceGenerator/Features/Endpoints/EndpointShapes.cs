using System.Collections.Immutable;

namespace Pragmatic.SourceGenerator.Features.Endpoints;

/// <summary>
///     The shapes an <c>[Endpoint]</c> class may take: the base types the transform recognises, and
///     the one attribute that stands in for a base type.
/// </summary>
/// <remarks>
///     <para>
///         One table, read twice. The transform matches base-type names against
///         <see cref="EndpointShape.MetadataPrefix" />; PRAG0501 lists <see cref="EndpointShape.Spelling" />
///         to the author whose class matched none of them. Two separate lists would let the message
///         age: a shape the transform accepts and the message never names sends whoever published it
///         with the wrong shape to an error listing forms that do not include theirs.
///     </para>
///     <para>
///         Order matters for the message only — it is the order a reader expects, not the order the
///         transform asks in.
///     </para>
/// </remarks>
internal static class EndpointShapes
{
    public static readonly EndpointShape Endpoint =
        new("Pragmatic.Endpoints.Base.Endpoint<", "Endpoint<T>");

    public static readonly EndpointShape VoidEndpoint =
        new("Pragmatic.Endpoints.Base.VoidEndpoint", "VoidEndpoint");

    public static readonly EndpointShape StreamingEndpoint =
        new("Pragmatic.Endpoints.Base.StreamingEndpoint<", "StreamingEndpoint<T>");

    public static readonly EndpointShape DomainAction =
        new("Pragmatic.Actions.Abstractions.DomainAction", "DomainAction<T>");

    public static readonly EndpointShape VoidDomainAction =
        new("Pragmatic.Actions.Abstractions.VoidDomainAction", "VoidDomainAction");

    public static readonly EndpointShape StreamingDomainAction =
        new("Pragmatic.Actions.Abstractions.StreamingDomainAction<", "StreamingDomainAction<T>");

    public static readonly EndpointShape Mutation =
        new("Pragmatic.Actions.Mutation.Mutation<", "Mutation<T>");

    /// <summary>Not a base type: the attribute that makes a query an endpoint.</summary>
    public static readonly EndpointShape Query =
        new("Pragmatic.Persistence.Query.Attributes.QueryAttribute", "[Query<TEntity, TResult>]");

    public static readonly ImmutableArray<EndpointShape> All = ImmutableArray.Create(
        Endpoint, VoidEndpoint, StreamingEndpoint,
        DomainAction, VoidDomainAction, StreamingDomainAction,
        Mutation, Query);

    /// <summary>Every accepted shape, spelled for a diagnostic: "A, B, or C".</summary>
    public static string Listed
    {
        get
        {
            var spellings = All.Select(shape => shape.Spelling).ToArray();
            return string.Join(", ", spellings.Take(spellings.Length - 1)) + ", or " + spellings[spellings.Length - 1];
        }
    }
}
