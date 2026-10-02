using Pragmatic.Authorization.Policy;
using Pragmatic.Authorization.Policy.Policies;
using Pragmatic.Identity;

namespace Pragmatic.Authorization.Serialization;

/// <summary>
///     Converts between <see cref="ResourcePolicy" /> and <see cref="PolicyExpression" />
///     for JSON-safe storage. Custom and delegate-based policies are not serializable.
/// </summary>
public static class PolicySerializer
{
    /// <summary>
    ///     Serializes a <see cref="ResourcePolicy" /> to a <see cref="PolicyExpression" />.
    /// </summary>
    /// <exception cref="NotSupportedException">Thrown for non-serializable policies (Custom).</exception>
    public static PolicyExpression Serialize(ResourcePolicy policy)
    {
        return policy switch
        {
            AllowPolicy => new PolicyExpression { Type = PolicyExpressionType.Allow },
            DenyPolicy => new PolicyExpression { Type = PolicyExpressionType.Deny },
            AuthenticatedPolicy => new PolicyExpression { Type = PolicyExpressionType.Authenticated },
            MfaPolicy => new PolicyExpression { Type = PolicyExpressionType.Mfa },

            PermissionPolicy p => new PolicyExpression
            {
                Type = PolicyExpressionType.Permission,
                Value = p.Permission
            },

            AnyPermissionPolicy p => new PolicyExpression
            {
                Type = PolicyExpressionType.AnyPermission,
                Values = p.Permissions
            },

            AllPermissionsPolicy p => new PolicyExpression
            {
                Type = PolicyExpressionType.AllPermissions,
                Values = p.Permissions
            },

            RolePolicy p => new PolicyExpression
            {
                Type = PolicyExpressionType.Role,
                Value = p.Role
            },

            GroupPolicy p => new PolicyExpression
            {
                Type = PolicyExpressionType.Group,
                Value = p.Group
            },

            ClaimPolicy p => new PolicyExpression
            {
                Type = PolicyExpressionType.Claim,
                ClaimType = p.ClaimType,
                Value = p.ClaimValue
            },

            PrincipalKindPolicy p => new PolicyExpression
            {
                Type = PolicyExpressionType.PrincipalKind,
                Value = p.Kind.ToString()
            },

            AndPolicy p => new PolicyExpression
            {
                Type = PolicyExpressionType.And,
                Children = [Serialize(p.Left), Serialize(p.Right)]
            },

            OrPolicy p => new PolicyExpression
            {
                Type = PolicyExpressionType.Or,
                Children = [Serialize(p.Left), Serialize(p.Right)]
            },

            NotPolicy p => new PolicyExpression
            {
                Type = PolicyExpressionType.Not,
                Children = [Serialize(p.Inner)]
            },

            CustomPolicy => throw new NotSupportedException(
                "CustomPolicy is not serializable. Use built-in policy types for serializable policies."),

            _ => throw new NotSupportedException(
                $"Policy type '{policy.GetType().Name}' is not serializable.")
        };
    }

    // Bounds recursion so a deeply-nested (malformed or hostile) And/Or/Not tree from an untrusted
    // policy store cannot blow the stack — StackOverflowException is uncatchable and crashes the process.
    private const int MaxDeserializationDepth = 64;

    /// <summary>
    ///     Deserializes a <see cref="PolicyExpression" /> back to a <see cref="ResourcePolicy" />.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the expression is malformed or nested too deeply.</exception>
    public static ResourcePolicy Deserialize(PolicyExpression expression)
        => Deserialize(expression, 0);

    private static ResourcePolicy Deserialize(PolicyExpression expression, int depth)
    {
        if (depth > MaxDeserializationDepth)
            throw new ArgumentException(
                $"Policy expression nesting exceeds the maximum depth of {MaxDeserializationDepth}; " +
                "this usually indicates a malformed or hostile serialized policy.");

        return expression.Type switch
        {
            PolicyExpressionType.Allow => ResourcePolicy.Allow,
            PolicyExpressionType.Deny => ResourcePolicy.Deny,
            PolicyExpressionType.Authenticated => ResourcePolicy.IsAuthenticated(),
            PolicyExpressionType.Mfa => ResourcePolicy.RequiresMfa(),

            PolicyExpressionType.Permission => ResourcePolicy.RequirePermission(
                expression.Value ?? throw new ArgumentException("Permission policy requires Value.")),

            PolicyExpressionType.AnyPermission => ResourcePolicy.RequireAnyPermission(
                expression.Values ?? throw new ArgumentException("AnyPermission policy requires Values.")),

            PolicyExpressionType.AllPermissions => ResourcePolicy.RequireAllPermissions(
                expression.Values ?? throw new ArgumentException("AllPermissions policy requires Values.")),

            PolicyExpressionType.Role => ResourcePolicy.InRole(
                expression.Value ?? throw new ArgumentException("Role policy requires Value.")),

            PolicyExpressionType.Group => ResourcePolicy.InGroup(
                expression.Value ?? throw new ArgumentException("Group policy requires Value.")),

            PolicyExpressionType.Claim => ResourcePolicy.HasClaim(
                expression.ClaimType ?? throw new ArgumentException("Claim policy requires ClaimType."),
                expression.Value),

            PolicyExpressionType.PrincipalKind => ResourcePolicy.HasPrincipalKind(
                ParsePrincipalKind(expression.Value)),

            PolicyExpressionType.And => DeserializeComposite(expression, depth, (l, r) => l & r),
            PolicyExpressionType.Or => DeserializeComposite(expression, depth, (l, r) => l | r),

            PolicyExpressionType.Not => !Deserialize(
                GetSingleChild(expression), depth + 1),

            _ => throw new ArgumentException($"Unknown policy expression type: {expression.Type}")
        };
    }

    private static ResourcePolicy DeserializeComposite(
        PolicyExpression expression,
        int depth,
        Func<ResourcePolicy, ResourcePolicy, ResourcePolicy> combine)
    {
        var children = expression.Children
            ?? throw new ArgumentException($"{expression.Type} policy requires Children.");

        if (children.Length < 2)
            throw new ArgumentException($"{expression.Type} policy requires at least 2 children.");

        var result = Deserialize(children[0], depth + 1);
        for (var i = 1; i < children.Length; i++)
            result = combine(result, Deserialize(children[i], depth + 1));

        return result;
    }

    private static PrincipalKind ParsePrincipalKind(string? value)
    {
        if (value is null)
            throw new ArgumentException("PrincipalKind policy requires Value.");
        if (!Enum.TryParse<PrincipalKind>(value, ignoreCase: true, out var kind))
            throw new ArgumentException(
                $"PrincipalKind policy Value '{value}' is not a valid PrincipalKind. Expected one of: {string.Join(", ", Enum.GetNames<PrincipalKind>())}.");
        return kind;
    }

    private static PolicyExpression GetSingleChild(PolicyExpression expression)
    {
        var children = expression.Children
            ?? throw new ArgumentException("Not policy requires Children.");

        if (children.Length != 1)
            throw new ArgumentException("Not policy requires exactly 1 child.");

        return children[0];
    }
}
