using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.EFCore.Samples.Lifecycle;

/// <summary>
///     A subscription whose business identifier is auto-generated.
///     <c>[GeneratedValue]</c> declares a human-readable key format — in host mode the SG wires
///     generation/validation; the format below produces values like <c>SUB-202605-00001</c>.
///     The <see cref="SubscriptionNumberGenerator"/> mirrors that template via
///     <c>IDefaultValueGenerator</c> so the sample can run it end-to-end.
/// </summary>
[Entity]
[Auditable]
public partial class Subscription : IEntity, IAuditable
{
    public Guid PersistenceId { get; set; } = Guid.CreateVersion7();

    public Guid Id => PersistenceId;

    /// <summary>Auto-generated business key. Format mirrored by <see cref="SubscriptionNumberGenerator"/>.</summary>
    [GeneratedValue("SUB-{YYYY}{MM}-{SEQ:5}")]
    public string SubscriptionNumber { get; set; } = "";

    public string PlanName { get; set; } = "";

    /// <summary>Defaulted by <see cref="SubscriptionLifecycle.OnCreating"/> when left at zero.</summary>
    public decimal MonthlyPrice { get; set; }

    public DateTimeOffset? RenewsOn { get; set; }

    // IAuditable
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}
