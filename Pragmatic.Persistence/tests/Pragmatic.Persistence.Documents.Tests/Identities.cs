using Pragmatic.Identity;
using Pragmatic.Identity.Local;
using Pragmatic.Persistence.Entity;

namespace PragmaticDocuments.Tests;

/// <summary>
///     An account that signs in locally, as an application's user does: a <c>[PragmaticUser]</c> owning Pragmatic.Identity.Local's own
///     <c>LocalIdentity</c> (optional, null when the document holds none), created from it at a self-registration. The generator writes its
///     <c>LocalIdentityStore</c>.
/// </summary>
[Entity]
[PragmaticUser(MatchClaim = "sub")]
public partial class Account : IEntity, ISelfRegisteringUser<Account>
{
    public string Name { get; set; } = "";

    public LocalIdentity? Identity { get; set; }

    public static Account Register(LocalIdentity identity)
    {
        var account = Create();
        account.Name = identity.Email ?? "";
        account.Identity = identity;
        return account;
    }
}
