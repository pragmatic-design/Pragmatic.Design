using Pragmatic.MultiTenancy;
using Pragmatic.Persistence.Entity;

namespace Pragmatic.Persistence.Benchmarks.Workload;

/// <summary>The row every save modifies: an audited, soft-deletable, tenant-owned entity.</summary>
public sealed class Ticket : IAuditable, ISoftDelete, ITenantEntity
{
    public Guid Id { get; set; }
    public Guid ProjectId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public decimal Amount { get; set; }
    public int Priority { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string? CreatedBy { get; set; }
    public DateTimeOffset? UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAt { get; set; }
    public string? DeletedBy { get; set; }
    public string TenantId { get; set; } = "";
}
