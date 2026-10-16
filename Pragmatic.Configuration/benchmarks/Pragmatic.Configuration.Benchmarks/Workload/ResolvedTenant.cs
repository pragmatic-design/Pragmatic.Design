using Pragmatic.MultiTenancy;

namespace Pragmatic.Configuration.Benchmarks.Workload;

/// <summary>The tenant a request resolved: the one whose store values overlay appsettings.</summary>
public sealed class ResolvedTenant : ITenantContext
{
    public const string Id = "tenant-it";

    public string? TenantId => Id;
    public string? TenantName => Id;
    public bool IsResolved => true;
}
