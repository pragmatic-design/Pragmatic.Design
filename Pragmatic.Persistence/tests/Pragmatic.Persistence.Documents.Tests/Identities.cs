using Pragmatic.Identity;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Identity
{
    /// <summary>How an identity was provisioned: an enum on the base class.</summary>
    public enum ProvisionSource
    {
        Manual = 0,
        Scim = 1,
    }

    /// <summary>
    ///     Pragmatic.Identity's base of every identity record, read by its name as the generator reads it (this suite does not reference the
    ///     package): no attribute marks it owned, and the members an application signs in by are declared here, not on the derived record.
    /// </summary>
    public abstract class IdentityRecord
    {
        public string ExternalIdentityKey { get; set; } = string.Empty;

        public ProvisionSource ProvisionSource { get; set; } = ProvisionSource.Manual;

        public bool IsActive { get; set; } = true;

        public DateTimeOffset? LastLoginAt { get; set; }
    }
}

namespace PragmaticDocuments.Tests
{
    /// <summary>A provider's record, as <c>LocalIdentity</c>: its own members beside the base class's, and a constant that is not a field.</summary>
    public sealed class Credentials : IdentityRecord
    {
        public const string Provider = "local";

        public string PasswordHash { get; set; } = string.Empty;

        public int FailedLoginAttempts { get; set; }
    }

    /// <summary>An account that may have no local sign-in: the record optional, null when the document holds none.</summary>
    [Entity]
    public partial class Account : IEntity
    {
        public string Name { get; set; } = "";

        public Credentials? Identity { get; set; }
    }
}
