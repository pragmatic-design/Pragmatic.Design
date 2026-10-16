using Pragmatic.MultiTenancy;

namespace Pragmatic.Persistence.Benchmarks.Workload;

/// <summary>A resolved tenant: the tenant interceptor enumerates the tracker only when there is one.</summary>
public sealed class ResolvedTenant : ITenantContext
{
    public const string Id = "bench-tenant";

    public string? TenantId => Id;
    public string? TenantName => "Bench";
    public bool IsResolved => true;
}
