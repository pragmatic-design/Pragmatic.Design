using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Pragmatic.Testing.SourceGenerator.Models;
using Pragmatic.Testing.SourceGenerator.Synthesis;

namespace Pragmatic.Testing.SourceGenerator.Transforms;

/// <summary>
///     The JSON body an operation takes, and whether the synthesiser could fill it — read once, for
///     every contract that has to post one.
/// </summary>
/// <remarks>
///     <para>
///         A create and a transition put the same question to the same shape: which of this endpoint's
///         members travel in the body, and what is a value the application will accept. The create half
///         asked it from the beginning; the transition half posted the route and nothing else, so an
///         operation that requires a body answered <b>415</b> before it could answer the 409 or the 2xx
///         the contract measures — measured on both of Invoicing's transitions.
///     </para>
///     <para>
///         ⚠️ One reader, not two. The rules for "is this a body member" are subtle enough — a route
///         parameter, a member the invoker fills, a foreign key that must not be invented — that a
///         second copy would start answering differently within a story or two.
///     </para>
/// </remarks>
internal static class RequestBodyReader
{
    /// <summary>
    ///     The body members of <paramref name="endpointType" />, with a synthesised value each.
    /// </summary>
    /// <param name="endpointType">The operation whose body is being read.</param>
    /// <param name="entity">
    ///     The entity the operation writes, when there is one: a validation attribute may sit on it
    ///     rather than on the request member, and a value that fails that validator is a 400 the
    ///     contract would report as a broken endpoint.
    /// </param>
    /// <param name="route">
    ///     The operation's route. A member whose name is one of its <c>{tokens}</c> is bound from the
    ///     path even with no attribute saying so, which is how an operation that loads by id is written.
    /// </param>
    /// <returns>
    ///     The fields that could be filled, and whether <b>every required</b> one could. When it could
    ///     not, the caller renders no body and asks the application for one through
    ///     <c>PragmaticContractHost.Body</c>.
    /// </returns>
    public static (ImmutableArray<CrudFieldModel> Fields, bool CanSynthesize) Read(
        INamedTypeSymbol endpointType,
        INamedTypeSymbol? entity,
        string route)
    {
        var fields = ImmutableArray.CreateBuilder<CrudFieldModel>();
        var canSynthesize = true;
        var routeTokens = RouteTokens(route);

        foreach (var property in endpointType.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.IsStatic || property.IsImplicitlyDeclared || property.SetMethod is null)
                continue;
            if (property.DeclaredAccessibility != Accessibility.Public)
                continue;
            if (IsBoundFromRequestPart(property))
                continue; // route/query/header parameter — not part of the JSON body
            if (routeTokens.Contains(property.Name))
                continue; // the route already carries it, whether or not an attribute says so

            var value = IsForeignKey(property)
                ? null
                : FormatAwareValue(property, entity) ?? TestDataSynthesizer.Synthesize(property.Type);

            if (value is null)
            {
                // A member we cannot fill. If it is optional, leaving it out still yields a valid body —
                // a nullable collection of a complex type must not sink an otherwise perfectly
                // synthesizable create. Only a required one makes the body unusable.
                if (CrudCreateExtractor.IsRequiredField(property))
                    canSynthesize = false;
                continue;
            }

            fields.Add(new CrudFieldModel { Name = property.Name, ValueExpression = value });
        }

        return (fields.ToImmutable(), canSynthesize);
    }

    /// <summary>
    ///     The <c>{tokens}</c> of a route, compared to member names the way model binding does: by name,
    ///     ignoring case.
    /// </summary>
    /// <remarks>
    ///     ⚠️ Nothing marks these members. An operation that loads by id declares
    ///     <c>public required Guid Id { get; init; }</c> and lets the path bind it — so read as a body
    ///     member it is a required <c>Guid</c> ending in <c>Id</c>, which is a foreign key the synthesiser
    ///     refuses to invent, and the whole body became unsynthesisable. Every transition written that way
    ///     would then have demanded a <c>BodyFor</c> for a request that carries no content at all.
    /// </remarks>
    private static HashSet<string> RouteTokens(string route)
    {
        var tokens = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        for (var open = route.IndexOf('{'); open >= 0; open = route.IndexOf('{', open + 1))
        {
            var close = route.IndexOf('}', open + 1);
            if (close < 0)
                break;

            // "{id:guid}" and "{slug?}" bind the same member as "{id}" — the constraint is not the name.
            var token = route.Substring(open + 1, close - open - 1);
            var cut = token.IndexOfAny([':', '?', '=']);
            if (cut >= 0)
                token = token.Substring(0, cut);

            if (token.Length > 0)
                tokens.Add(token);

            open = close;
        }

        return tokens;
    }

    /// <summary>
    ///     Whether the member travels somewhere other than the body — a route, query or header
    ///     parameter, or a value the operation's invoker fills.
    /// </summary>
    /// <remarks>
    ///     ⚠️ <c>[FromClock]</c>, <c>[FromCurrentUser]</c> and <c>[FromClaim]</c> are in the list because a
    ///     caller does not send them: the invoker writes them, and a contract that posted one would be
    ///     describing a request nobody makes. <c>[FromClaim]</c> was missing, and a claim-bound
    ///     <c>Guid …Id</c> read as a foreign key made a request with no content demand a body. They are named rather than inferred from the setter's accessibility —
    ///     that reading would also drop an ordinary <c>{ get; private set; }</c> member the application
    ///     does accept, which is a body field the create contract has always sent.
    /// </remarks>
    private static bool IsBoundFromRequestPart(IPropertySymbol property) =>
        property.GetAttributes().Any(a => a.AttributeClass?.Name is
            "FromRouteAttribute" or "FromQueryAttribute" or "FromHeaderAttribute"
            or "FromClockAttribute" or "FromCurrentUserAttribute" or "FromClaimAttribute");

    /// <summary>
    ///     A <c>Guid</c> whose name reads as a foreign key. Deliberately not invented: a random one
    ///     references a row that does not exist, and the contract would then fail on a 404 that says
    ///     nothing about the operation.
    /// </summary>
    private static bool IsForeignKey(IPropertySymbol property)
        => CrudCreateExtractor.IsForeignKey(property);

    /// <inheritdoc cref="CrudCreateExtractor.FormatAwareValue" />
    private static string? FormatAwareValue(IPropertySymbol property, INamedTypeSymbol? entity)
        => CrudCreateExtractor.FormatAwareValue(property, entity);
}
