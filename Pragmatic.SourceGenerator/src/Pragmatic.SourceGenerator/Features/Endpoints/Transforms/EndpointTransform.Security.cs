using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Pragmatic.SourceGenerator.Features.Endpoints.Models;

namespace Pragmatic.SourceGenerator.Features.Endpoints.Transforms;

/// <summary>
///     Security-related parsing methods (Authorization, RateLimit, ResponseCache).
/// </summary>
internal static partial class EndpointTransform
{
    /// <summary>
    ///     The resource policy declared with <c>[RequirePolicy&lt;T&gt;]</c>, or <c>null</c>.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Recorded so the generated query endpoint can evaluate the policy itself.
    ///         <c>PolicyEvaluationFilter</c> is an <c>IActionFilter</c> at Order 210, and a query's invoker
    ///         does not run the action-filter chain: without the endpoint's own check, a policy declared
    ///         on a query would be inert and nothing would say so.
    ///     </para>
    ///     <para>
    ///         Matched by name and namespace, never by <c>ToDisplayString()</c> — the attribute is
    ///         generic, so its display string carries the type argument and no equality against a
    ///         constant can match it.
    ///     </para>
    /// </remarks>
    private static string? ParseResourcePolicy(INamedTypeSymbol symbol)
    {
        foreach (var attr in symbol.GetAttributes())
        {
            var attrClass = attr.AttributeClass;
            if (attrClass is null || attrClass.Name != "RequirePolicyAttribute")
                continue;

            if (attrClass.ContainingNamespace?.ToDisplayString() != "Pragmatic.Authorization.Policy")
                continue;

            // Fully qualified: the generated endpoint constructs this type, and a name that happens to
            // resolve in the declaring file is not one that resolves in a generated one.
            return attrClass.TypeArguments.Length > 0
                ? attrClass.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                : null;
        }

        return null;
    }

    /// <summary>
    ///     Reads the authorization attributes. Permission arguments go through
    ///     <see cref="Actions.Transforms.ActionTransform.ExtractPermissionStrings" />, which drives from
    ///     the argument syntax rather than from <c>ConstructorArguments</c>: a constant this generator
    ///     emits in the same compilation cannot be bound, and reading only the bound values would drop
    ///     it — leaving the endpoint with no authorization at all. Such a path is kept as written and
    ///     resolved later against the permission catalog.
    /// </summary>
    private static AuthorizationModel? ParseAuthorization(INamedTypeSymbol symbol, Compilation compilation)
    {
        var requiredPerms = ImmutableArray<string>.Empty;
        var anyPerms = ImmutableArray<string>.Empty;
        var unresolvedRequired = ImmutableArray<string>.Empty;
        var unresolvedAny = ImmutableArray<string>.Empty;
        var allowAnonymous = false;
        string? policyName = null;

        foreach (var attr in symbol.GetAttributes())
        {
            var attrName = attr.AttributeClass?.ToDisplayString();

            if (attrName is EndpointAttributeNames.RequirePermission or EndpointAttributeNames.LegacyRequirePermission)
                (requiredPerms, unresolvedRequired) = ExtractPermissions(attr, compilation);
            else if (attrName is EndpointAttributeNames.RequireAnyPermission or EndpointAttributeNames.LegacyRequireAnyPermission)
                (anyPerms, unresolvedAny) = ExtractPermissions(attr, compilation);
            else if (attrName == EndpointAttributeNames.AllowAnonymous ||
                     attrName == EndpointAttributeNames.MicrosoftAllowAnonymous)
                allowAnonymous = true;
            else if (attrName == EndpointAttributeNames.MicrosoftAuthorize)
                foreach (var namedArg in attr.NamedArguments)
                    if (namedArg.Key == "Policy")
                        policyName = namedArg.Value.Value?.ToString();
        }

        var hasPermissions = !requiredPerms.IsEmpty || !anyPerms.IsEmpty
                             || !unresolvedRequired.IsEmpty || !unresolvedAny.IsEmpty;

        // [TenantAgnostic] on its own counts. This model is what carries route-level declarations to
        // every handler renderer, and without it in the condition an operation that declares only
        // "this route belongs to no tenant" would produce no model at all — the declaration read and
        // then dropped.
        var tenantAgnostic = Actions.Transforms.ActionTransform.ParseTenantAgnostic(symbol);

        if (hasPermissions || allowAnonymous || policyName is not null || tenantAgnostic)
            return new AuthorizationModel
            {
                IsRequired = hasPermissions || policyName is not null,
                AllowAnonymous = allowAnonymous,
                RouteIsTenantAgnostic = tenantAgnostic,
                PolicyName = policyName,
                RequiredPermissions = requiredPerms,
                AnyPermissions = anyPerms,
                UnresolvedRequiredPermissionPaths = unresolvedRequired,
                UnresolvedAnyPermissionPaths = unresolvedAny
            };

        return null;
    }

    /// <summary>
    ///     Splits a permission attribute into the values that bound and the constant paths that did
    ///     not. Empty bound values are discarded: an empty permission grants everyone.
    /// </summary>
    private static (ImmutableArray<string> Resolved, ImmutableArray<string> Unresolved) ExtractPermissions(
        AttributeData attr, Compilation compilation)
    {
        var (resolved, unresolved) = Actions.Transforms.ActionTransform.ExtractPermissionStrings(attr, compilation);
        return (resolved.Where(static s => !string.IsNullOrEmpty(s)).ToImmutableArray(), unresolved);
    }

    private static RateLimitModel? ParseRateLimit(INamedTypeSymbol symbol)
    {
        var attr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == EndpointAttributeNames.RateLimit);

        if (attr is null)
            return null;

        var requests = 0;
        string? window = null;
        string? policy = null;

        foreach (var namedArg in attr.NamedArguments)
            switch (namedArg.Key)
            {
                case "Requests":
                    requests = (int)(namedArg.Value.Value ?? 0);
                    break;
                case "Window":
                    window = namedArg.Value.Value?.ToString();
                    break;
                case "Policy":
                    policy = namedArg.Value.Value?.ToString();
                    break;
            }

        return new RateLimitModel
        {
            Policy = policy,
            Requests = requests,
            Window = window
        };
    }

    private static ResponseCacheModel? ParseResponseCache(INamedTypeSymbol symbol)
    {
        var attr = symbol.GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == EndpointAttributeNames.ResponseCache);

        if (attr is null)
            return null;

        var duration = 0;
        var location = "Any";
        var noStore = false;
        var varyByQuery = ImmutableArray<string>.Empty;
        var varyByHeaders = ImmutableArray<string>.Empty;
        string? profile = null;

        foreach (var namedArg in attr.NamedArguments)
            switch (namedArg.Key)
            {
                case "Duration":
                    duration = (int)(namedArg.Value.Value ?? 0);
                    break;
                case "Location":
                    // An enum-typed argument exposes its boxed *numeric* value, not the
                    // member name — resolve the symbolic name ("Any"/"Client"/"None") so the
                    // template can honor the documented cache-control semantics.
                    location = ResolveEnumMemberName(namedArg.Value) ?? "Any";
                    break;
                case "NoStore":
                    noStore = (bool)(namedArg.Value.Value ?? false);
                    break;
                case "VaryByQueryKeys":
                    varyByQuery = namedArg.Value.Values
                        .Select(v => v.Value?.ToString() ?? string.Empty)
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToImmutableArray();
                    break;
                case "VaryByHeaders":
                    varyByHeaders = namedArg.Value.Values
                        .Select(v => v.Value?.ToString() ?? string.Empty)
                        .Where(s => !string.IsNullOrEmpty(s))
                        .ToImmutableArray();
                    break;
                case "Profile":
                    profile = namedArg.Value.Value?.ToString();
                    break;
            }

        return new ResponseCacheModel
        {
            Duration = duration,
            Location = location,
            NoStore = noStore,
            VaryByQueryKeys = varyByQuery,
            VaryByHeaders = varyByHeaders,
            Profile = profile
        };
    }

    /// <summary>
    ///     Resolves the symbolic member name of an enum-typed attribute argument
    ///     (a <see cref="TypedConstant" /> exposes the boxed underlying numeric value, not the name).
    /// </summary>
    private static string? ResolveEnumMemberName(TypedConstant value)
    {
        if (value.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType)
            foreach (var member in enumType.GetMembers().OfType<IFieldSymbol>())
                if (member.HasConstantValue && Equals(member.ConstantValue, value.Value))
                    return member.Name;

        return value.Value?.ToString();
    }
}
