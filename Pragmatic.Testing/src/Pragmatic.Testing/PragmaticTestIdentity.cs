using System.Collections.Generic;

namespace Pragmatic.Testing;

/// <summary>
///     Sets the dev-identity headers a Pragmatic host trusts when bearer auth is off (the <c>NoOp</c> dev
///     handler): <c>X-User-Id</c>, <c>X-User-Name</c>, <c>X-Tenant-Id</c>, <c>X-User-Roles</c>,
///     <c>X-User-Groups</c>, <c>X-User-Permissions</c>. Lets a contract test authenticate as a specific
///     user/tenant with specific permissions — including the deliberately-underprivileged caller used to
///     assert a rejection (#7).
///     <para>
///         <b>Drive roles, not permissions, against a host that calls <c>UseAuthorization(authz =&gt; …)</c>.</b>
///         That call installs a resolver which derives permissions from the role map, and raw permission
///         claims stop being honoured — so a caller carrying the exact permission still gets 403. It reads
///         like a framework defect, and it is why <c>roles</c> is expressible here instead of only by
///         writing the header by hand.
///     </para>
///     <para>
///         <c>tenantId</c> writes <c>X-Tenant-Id</c>, which is what tenant <em>resolution</em> reads. It is
///         not the tenant claim on the principal — <c>HeaderUserMiddleware</c> takes that from
///         <c>X-User-Tenant</c>. Two mechanisms, deliberately.
///     </para>
///     <para>
///         On a single request the permission header is always written (empty when no permission is given), so
///         it overrides any grant the client carries in its <c>DefaultRequestHeaders</c>. Without that, an
///         "underprivileged" caller would silently inherit the fixture's grant.
///     </para>
/// </summary>
public static class PragmaticTestIdentity
{
    public const string UserIdHeader = "X-User-Id";
    public const string UserNameHeader = "X-User-Name";
    public const string TenantIdHeader = "X-Tenant-Id";
    public const string PermissionsHeader = "X-User-Permissions";
    public const string RolesHeader = "X-User-Roles";
    public const string GroupsHeader = "X-User-Groups";

    /// <summary>Sets the default identity headers on the client for every request it sends.</summary>
    public static HttpClient AsUser(
        this HttpClient client,
        string userId,
        string? tenantId = null,
        string? userName = null,
        string[]? permissions = null,
        string[]? roles = null,
        string[]? groups = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        // No underlying layer to mask here: an absent permissions header means "grant nothing".
        Apply(client.DefaultRequestHeaders.Add, userId, tenantId, userName, permissions, roles, groups,
            alwaysWritePermissions: false);
        return client;
    }

    /// <summary>
    ///     Sets the identity headers on a single request.
    ///     <para>
    ///         The permission header is <b>always</b> written, empty when no permission is supplied. This is what
    ///         makes an underprivileged caller actually underprivileged: <see cref="HttpClient"/> copies its
    ///         <c>DefaultRequestHeaders</c> onto every request that does not already carry them, so a merely
    ///         omitted header would let the client's default grant (often a wildcard in a shared test fixture)
    ///         through and silently authorize the call. An empty value suppresses that default and yields no
    ///         permission claim server-side.
    ///     </para>
    ///     <para>
    ///         The optional user-name and tenant headers keep the opposite behavior on purpose: when not supplied
    ///         they fall back to the client defaults, so a contract test inherits the fixture's tenant instead of
    ///         losing it.
    ///     </para>
    /// </summary>
    public static HttpRequestMessage AsUser(
        this HttpRequestMessage request,
        string userId,
        string? tenantId = null,
        string? userName = null,
        string[]? permissions = null,
        string[]? roles = null,
        string[]? groups = null)
    {
        ArgumentNullException.ThrowIfNull(request);
        Apply(
            (name, value) =>
            {
                // Add() rejects/validates some empty values; TryAddWithoutValidation reliably materializes the
                // header so HttpClient sees it as "already present" and skips its default.
                if (value.Length == 0)
                    request.Headers.TryAddWithoutValidation(name, value);
                else
                    request.Headers.Add(name, value);
            },
            userId, tenantId, userName, permissions, roles, groups, alwaysWritePermissions: true);
        return request;
    }

    private static void Apply(
        Action<string, string> add,
        string userId,
        string? tenantId,
        string? userName,
        IReadOnlyList<string>? permissions,
        IReadOnlyList<string>? roles,
        IReadOnlyList<string>? groups,
        bool alwaysWritePermissions)
    {
        add(UserIdHeader, userId);
        if (!string.IsNullOrEmpty(userName))
            add(UserNameHeader, userName!);
        if (!string.IsNullOrEmpty(tenantId))
            add(TenantIdHeader, tenantId!);

        if (roles is { Count: > 0 })
            add(RolesHeader, string.Join(",", roles));
        if (groups is { Count: > 0 })
            add(GroupsHeader, string.Join(",", groups));

        if (permissions is { Count: > 0 })
            add(PermissionsHeader, string.Join(",", permissions));
        else if (alwaysWritePermissions)
            add(PermissionsHeader, string.Empty);
    }
}
