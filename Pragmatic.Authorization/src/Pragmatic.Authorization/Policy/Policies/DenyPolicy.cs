using Pragmatic.Identity;

namespace Pragmatic.Authorization.Policy.Policies;

/// <summary>
///     A policy that always denies access. Singleton identity element for AND composition.
/// </summary>
internal sealed class DenyPolicy : ResourcePolicy
{
    public static readonly DenyPolicy Instance = new();

    private DenyPolicy() { }

    public override bool Evaluate(ICurrentUser user) => false;
}
