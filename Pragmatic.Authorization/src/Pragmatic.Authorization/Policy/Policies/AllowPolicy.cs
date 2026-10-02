using Pragmatic.Identity;

namespace Pragmatic.Authorization.Policy.Policies;

/// <summary>
///     A policy that always allows access. Singleton identity element for OR composition.
/// </summary>
internal sealed class AllowPolicy : ResourcePolicy
{
    public static readonly AllowPolicy Instance = new();

    private AllowPolicy() { }

    public override bool Evaluate(ICurrentUser user) => true;
}
