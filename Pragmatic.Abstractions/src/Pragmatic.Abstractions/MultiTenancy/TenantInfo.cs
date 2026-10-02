namespace Pragmatic.MultiTenancy;

/// <summary>
///     Metadata for a registered tenant.
///     When <see cref="ConnectionString"/> is non-null, the tenant uses a dedicated database (DB-per-tenant).
///     When null, the tenant shares the default database (row-level isolation via TenantId filter).
/// </summary>
public sealed record TenantInfo
{
    /// <summary>Unique tenant identifier (matches <see cref="ITenantContext.TenantId"/>).</summary>
    public required string TenantId { get; init; }

    /// <summary>Human-readable tenant name.</summary>
    public required string TenantName { get; init; }

    /// <summary>
    ///     Dedicated connection string for this tenant.
    ///     Null means the tenant uses the shared (default) database with row-level isolation.
    /// </summary>
    public string? ConnectionString { get; init; }

    /// <summary>Current lifecycle state.</summary>
    public required TenantState State { get; init; }

    /// <summary>When this tenant was registered.</summary>
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Extensible key-value metadata (plan, region, feature flags, etc.).</summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; } =
        new Dictionary<string, string>();

    /// <summary>
    ///     Excludes <see cref="ConnectionString"/> from the default record ToString
    ///     to prevent accidental logging of credentials.
    /// </summary>
    private bool PrintMembers(System.Text.StringBuilder builder)
    {
        builder.Append($"TenantId = {TenantId}, TenantName = {TenantName}, ");
        builder.Append($"ConnectionString = {(ConnectionString is null ? "null" : "***")}, ");
        builder.Append($"State = {State}, CreatedAt = {CreatedAt}");
        return true;
    }
}
