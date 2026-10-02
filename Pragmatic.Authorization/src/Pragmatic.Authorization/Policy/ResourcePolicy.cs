using Pragmatic.Authorization.Policy.Policies;
using Pragmatic.Identity;

namespace Pragmatic.Authorization.Policy;

/// <summary>
///     Composable authorization rule that evaluates against the current user.
///     Analogous to <c>Specification&lt;T&gt;</c> but for authorization decisions
///     instead of entity predicates.
/// </summary>
/// <remarks>
///     <para>
///         Use operator overloads for fluent composition:
///         <c>ResourcePolicy.RequirePermission("orders.read") &amp; ResourcePolicy.IsAuthenticated()</c>
///     </para>
///     <para>
///         For async evaluation (e.g., external permission checks), use <see cref="AsyncResourcePolicy" />.
///     </para>
/// </remarks>
public abstract class ResourcePolicy
{
    /// <summary>
    ///     Evaluates whether the given user satisfies this policy.
    /// </summary>
    /// <param name="user">The current user to evaluate against.</param>
    /// <returns>True if the user satisfies the policy; otherwise, false.</returns>
    public abstract bool Evaluate(ICurrentUser user);

    // =========================================================================
    // Composition
    // =========================================================================

    /// <summary>Combines this policy with another using logical AND.</summary>
    public ResourcePolicy And(ResourcePolicy other) => new AndPolicy(this, other);

    /// <summary>Combines this policy with another using logical OR.</summary>
    public ResourcePolicy Or(ResourcePolicy other) => new OrPolicy(this, other);

    /// <summary>Negates this policy.</summary>
    public ResourcePolicy Not() => new NotPolicy(this);

    /// <summary>Logical AND operator for policy composition.</summary>
    public static ResourcePolicy operator &(ResourcePolicy left, ResourcePolicy right) => left.And(right);

    /// <summary>Logical OR operator for policy composition.</summary>
    public static ResourcePolicy operator |(ResourcePolicy left, ResourcePolicy right) => left.Or(right);

    /// <summary>Logical NOT operator for policy negation.</summary>
    public static ResourcePolicy operator !(ResourcePolicy policy) => policy.Not();

    // =========================================================================
    // Identity elements
    // =========================================================================

    /// <summary>A policy that always allows access.</summary>
    public static ResourcePolicy Allow => AllowPolicy.Instance;

    /// <summary>A policy that always denies access.</summary>
    public static ResourcePolicy Deny => DenyPolicy.Instance;

    // =========================================================================
    // Factory methods
    // =========================================================================

    /// <summary>Requires the user to have the specified permission.</summary>
    public static ResourcePolicy RequirePermission(string permission) => new PermissionPolicy(permission);

    /// <summary>Requires the user to have at least one of the specified permissions (OR logic).</summary>
    public static ResourcePolicy RequireAnyPermission(params string[] permissions) => new AnyPermissionPolicy(permissions);

    /// <summary>Requires the user to have all of the specified permissions (AND logic).</summary>
    public static ResourcePolicy RequireAllPermissions(params string[] permissions) => new AllPermissionsPolicy(permissions);

    /// <summary>Requires the user to belong to the specified role.</summary>
    public static ResourcePolicy InRole(string role) => new RolePolicy(role);

    /// <summary>Requires the user to belong to the specified group.</summary>
    public static ResourcePolicy InGroup(string group) => new GroupPolicy(group);

    /// <summary>Requires the user to have a claim with the specified type and optional value.</summary>
    public static ResourcePolicy HasClaim(string claimType, string? claimValue = null) => new ClaimPolicy(claimType, claimValue);

    /// <summary>Requires the user to be authenticated.</summary>
    public static ResourcePolicy IsAuthenticated() => AuthenticatedPolicy.Instance;

    /// <summary>Requires the user to have the specified principal kind.</summary>
    public static ResourcePolicy HasPrincipalKind(PrincipalKind kind) => new PrincipalKindPolicy(kind);

    /// <summary>
    ///     Requires the user to have completed multi-factor authentication
    ///     (<c>ICurrentUser.Authentication.IsMfaAuthenticated</c>).
    /// </summary>
    public static ResourcePolicy RequiresMfa() => MfaPolicy.Instance;

    /// <summary>
    ///     Creates a custom policy from a delegate. Not serializable.
    /// </summary>
    public static ResourcePolicy Custom(Func<ICurrentUser, bool> predicate) => new CustomPolicy(predicate);
}
