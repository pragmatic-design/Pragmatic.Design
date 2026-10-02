namespace Pragmatic.Authorization.Serialization;

/// <summary>
///     Serializable representation of a <see cref="Policy.ResourcePolicy" /> tree.
///     Designed for JSON round-trip storage (e.g., database, config).
/// </summary>
public sealed record PolicyExpression
{
    /// <summary>
    ///     The type of policy node.
    /// </summary>
    public required PolicyExpressionType Type { get; init; }

    /// <summary>
    ///     Value used by single-value policies (Permission, Role, Group, PrincipalKind).
    /// </summary>
    public string? Value { get; init; }

    /// <summary>
    ///     Values used by multi-value policies (AnyPermission, AllPermissions).
    /// </summary>
    public string[]? Values { get; init; }

    /// <summary>
    ///     Claim type for <see cref="PolicyExpressionType.Claim" /> policies.
    /// </summary>
    public string? ClaimType { get; init; }

    /// <summary>
    ///     Child nodes for composite policies (And, Or, Not).
    /// </summary>
    public PolicyExpression[]? Children { get; init; }
}

/// <summary>
///     Discriminator for <see cref="PolicyExpression" /> nodes.
/// </summary>
public enum PolicyExpressionType
{
    /// <summary>Always allows.</summary>
    Allow,

    /// <summary>Always denies.</summary>
    Deny,

    /// <summary>Requires a specific permission.</summary>
    Permission,

    /// <summary>Requires any of the specified permissions.</summary>
    AnyPermission,

    /// <summary>Requires all of the specified permissions.</summary>
    AllPermissions,

    /// <summary>Requires a specific role.</summary>
    Role,

    /// <summary>Requires a specific group.</summary>
    Group,

    /// <summary>Requires a claim with type and optional value.</summary>
    Claim,

    /// <summary>Requires the user to be authenticated.</summary>
    Authenticated,

    /// <summary>Requires a specific principal kind.</summary>
    PrincipalKind,

    /// <summary>Requires the user to have completed multi-factor authentication.</summary>
    Mfa,

    /// <summary>Logical AND of children.</summary>
    And,

    /// <summary>Logical OR of children.</summary>
    Or,

    /// <summary>Logical NOT of a single child.</summary>
    Not
}
