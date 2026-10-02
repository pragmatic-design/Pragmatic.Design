using System.Collections.Immutable;
using Pragmatic.SourceGenerator.Core;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;
using Pragmatic.SourceGenerator.Features.Privacy.Models;

namespace Pragmatic.SourceGenerator.Features.Privacy.Transforms;

/// <summary>
///     Finds special-category properties that an endpoint exposes without constraining who may read them.
/// </summary>
/// <remarks>
///     <para>
///         This is the one Privacy diagnostic that cannot be decided from the entity alone. Whether
///         health data is over-exposed depends on what reaches it, and only the endpoint knows that —
///         which is why it lives here rather than in the entity transform.
///     </para>
///     <para>
///         <b>Authenticated is not the same as authorized.</b> An endpoint marked only "requires a
///         signed-in user" lets every account in the system read the data. For ordinary personal data
///         that is a defensible default; for special categories it is the case worth naming, so
///         authentication without a policy, permission or role still reports.
///     </para>
/// </remarks>
internal static class SpecialCategoryExposure
{
    /// <summary>One reported exposure: what is exposed, and by which endpoint.</summary>
    internal readonly struct Finding(PrivacyEntityModel entity, ClassifiedPropertyModel property, EndpointModel endpoint)
    {
        public PrivacyEntityModel Entity { get; } = entity;
        public ClassifiedPropertyModel Property { get; } = property;
        public EndpointModel Endpoint { get; } = endpoint;

        /// <summary>Reported at the endpoint: that is where the fix goes.</summary>
        public LocationInfo? Location => Endpoint.LocationInfo ?? Property.Location ?? Entity.Location;
    }

    public static List<Finding> Find(
        IReadOnlyList<PrivacyEntityModel> entities, ImmutableArray<EndpointModel> endpoints)
    {
        var findings = new List<Finding>();
        if (endpoints.IsDefaultOrEmpty)
            return findings;

        var special = new Dictionary<string, PrivacyEntityModel>(StringComparer.Ordinal);
        foreach (var entity in entities)
            if (HasSpecialCategory(entity))
                special[entity.FullTypeName] = entity;

        if (special.Count == 0)
            return findings;

        foreach (var endpoint in endpoints)
        {
            if (IsConstrained(endpoint.Authorization))
                continue;

            foreach (var touched in TypesTouchedBy(endpoint))
            {
                if (touched is null || !special.TryGetValue(WithoutGlobalPrefix(touched), out var entity))
                    continue;

                foreach (var property in entity.Properties)
                    if (property.Classification is { IsSpecialCategory: true })
                        findings.Add(new Finding(entity, property, endpoint));
            }
        }

        return findings;
    }

    private static bool HasSpecialCategory(PrivacyEntityModel entity)
    {
        foreach (var property in entity.Properties)
            if (property.Classification is { IsSpecialCategory: true })
                return true;

        return false;
    }

    /// <summary>
    ///     The declared entity types, not the response DTO shape.
    /// </summary>
    /// <remarks>
    ///     <c>ResponseType</c> is included last and matched by exact name like the others: it is usually a
    ///     DTO and will not match, but an endpoint that returns the entity directly is exactly the case
    ///     that should not slip through.
    /// </remarks>
    private static IEnumerable<string?> TypesTouchedBy(EndpointModel endpoint)
    {
        yield return endpoint.QueryEntityType;
        yield return endpoint.MutationEntityType;
        yield return endpoint.ResponseType;
    }

    /// <summary>The endpoint's spelling of a type, in the privacy model's: without <c>global::</c>.</summary>
    /// <remarks>
    ///     The endpoint transform writes entity types fully qualified (<c>global::App.Patient</c>), the privacy
    ///     transform without the prefix (<c>App.Patient</c>). Matched as written, no real endpoint ever named an
    ///     entity, and PRAG2904 was never reported from source.
    /// </remarks>
    private static string WithoutGlobalPrefix(string type)
        => type.StartsWith("global::", StringComparison.Ordinal) ? type.Substring("global::".Length) : type;

    /// <summary>
    ///     Whether the endpoint names <em>who</em> may reach it, as opposed to merely requiring a session.
    /// </summary>
    private static bool IsConstrained(AuthorizationModel? authorization)
    {
        if (authorization is null || authorization.AllowAnonymous)
            return false;

        return !string.IsNullOrWhiteSpace(authorization.PolicyName)
            || authorization.RequiredPermissions.Count > 0
            || authorization.AnyPermissions.Count > 0
            || authorization.RequiredRoles.Count > 0;
    }
}
