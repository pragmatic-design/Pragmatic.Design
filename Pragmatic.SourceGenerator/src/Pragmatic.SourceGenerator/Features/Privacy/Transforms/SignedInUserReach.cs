using System.Collections.Immutable;
using Pragmatic.SourceGen;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;
using Pragmatic.SourceGenerator.Features.Identity.Models;

namespace Pragmatic.SourceGenerator.Features.Privacy.Transforms;

/// <summary>
///     The signed-in user's entity, added to what an operation declaring <c>[LoadCurrentUser]</c> reaches.
/// </summary>
/// <remarks>
///     Which entity that is — the one <c>[PragmaticUser]</c> — is a fact of the compilation, not of the
///     operation, so it arrives through the pipeline from Identity rather than being looked for from the
///     operation's symbol. With none, or several (PRAG0451 on the operation already), nothing is added: the
///     register lists what is derived, not a guess.
/// </remarks>
internal static class SignedInUserReach
{
    public static ImmutableArray<EndpointModel> Resolve(
        ImmutableArray<EndpointModel> endpoints, EquatableArray<UserEntityModel> users)
    {
        if (users.Count != 1 || !endpoints.Any(e => e.LoadsCurrentUser))
            return endpoints;

        var user = users[0];
        var entity = string.IsNullOrEmpty(user.Namespace)
            ? $"global::{user.TypeName}"
            : $"global::{user.Namespace}.{user.TypeName}";

        return endpoints
            .Select(e => !e.LoadsCurrentUser || e.InferredEntityTypes.Contains(entity)
                ? e
                : e with
                {
                    InferredEntityTypes = Sorted(e.InferredEntityTypes, entity),
                    DomainActionEntityTypes = e.IsDomainAction
                        ? Sorted(e.DomainActionEntityTypes, entity)
                        : e.DomainActionEntityTypes
                })
            .ToImmutableArray();
    }

    private static ImmutableArray<string> Sorted(EquatableArray<string> existing, string added)
    {
        var all = existing.Where(e => e != added).Append(added).ToList();
        all.Sort(StringComparer.Ordinal);
        return ImmutableArray.CreateRange(all);
    }
}
