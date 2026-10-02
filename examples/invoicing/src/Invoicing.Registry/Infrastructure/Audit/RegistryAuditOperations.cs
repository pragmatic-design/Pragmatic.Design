namespace Invoicing.Registry.Infrastructure.Audit;

/// <summary>
///     The names a company's life is recorded under in the audit trail — constants, never assembled at
///     runtime, so a reader can search for them.
/// </summary>
/// <remarks>
///     ⚠️ Stored data, like Billing's: a trail already written keeps the old spelling, so these names are
///     additive and never renamed.
/// </remarks>
public static class RegistryAuditOperations
{
    public const string OrganizationOnboarded = "Registry.OrganizationOnboarded";
    public const string OrganizationSuspended = "Registry.OrganizationSuspended";
    public const string CustomerRegistered = "Registry.CustomerRegistered";

    /// <summary>What the audit trail calls a company.</summary>
    public const string OrganizationTarget = nameof(Entities.Organization);

    /// <summary>What the audit trail calls somebody a company bills.</summary>
    public const string CustomerTarget = nameof(Entities.Customer);
}
