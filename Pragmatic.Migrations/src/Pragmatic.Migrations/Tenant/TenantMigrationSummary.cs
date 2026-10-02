namespace Pragmatic.Migrations.Tenant;

/// <summary>
///     Result of a batch tenant migration run.
/// </summary>
/// <remarks>
///     ⚠️ Read <see cref="NotVisited" /> as well as the counts. A run accounts for the databases it
///     <b>reached</b>, and an operator migrating N customers needs it to account for N — which is not
///     the same number.
/// </remarks>
public sealed record TenantMigrationSummary
{
    /// <summary>Tenants the sweep set out to migrate: active, with a database of their own.</summary>
    public required int TotalTenants { get; init; }

    public required int SuccessCount { get; init; }

    public required int FailureCount { get; init; }

    /// <summary>
    ///     How many of <see cref="TotalTenants" /> produced no result because the run stopped early.
    /// </summary>
    /// <remarks>
    ///     A subset of <see cref="NotVisited" />, kept because callers count on it. The names are there.
    /// </remarks>
    public required int SkippedCount { get; init; }

    public required IReadOnlyList<TenantMigrationResult> Results { get; init; }

    /// <summary>
    ///     Every tenant with a database of its own that this run did <b>not</b> migrate, and why.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Two kinds of absence, both named here. A run that
    ///         stops on a failure leaves tenants it never attempted — counted by
    ///         <see cref="SkippedCount" />, and a count alone ("3 of 5 skipped") leaves an operator
    ///         with no way to know which three. And the sweep visits <b>active</b> tenants, so one still
    ///         provisioning, suspended, or left <c>Migrating</c> by an earlier failure has a database
    ///         that exists and was never looked at — which would otherwise appear nowhere at all, not
    ///         even as a count.
    ///     </para>
    ///     <para>
    ///         ⚠️ A tenant on the <b>shared</b> database is not here. It has nothing of its own to
    ///         migrate, so its absence from the run is not an absence.
    ///     </para>
    ///     <para>
    ///         Empty when the run reached everything, which is the state a green deployment is in.
    ///     </para>
    /// </remarks>
    public IReadOnlyList<TenantNotVisited> NotVisited { get; init; } = [];
}
