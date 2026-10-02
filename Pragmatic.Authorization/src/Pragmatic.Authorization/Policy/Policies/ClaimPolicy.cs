using Pragmatic.Identity;

namespace Pragmatic.Authorization.Policy.Policies;

/// <summary>
///     Requires the user to have a claim with the specified type and optional value.
/// </summary>
internal sealed class ClaimPolicy(string claimType, string? claimValue) : ResourcePolicy
{
    internal string ClaimType { get; } = claimType ?? throw new ArgumentNullException(nameof(claimType));
    internal string? ClaimValue { get; } = claimValue;

    public override bool Evaluate(ICurrentUser user)
    {
        if (!user.Claims.TryGetValue(ClaimType, out var values))
            return false;

        // If no specific value required, just check the claim exists
        if (ClaimValue is null)
            return true;

        return values.Contains(ClaimValue);
    }
}
