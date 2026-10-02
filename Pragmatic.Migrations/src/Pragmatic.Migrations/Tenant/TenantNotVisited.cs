namespace Pragmatic.Migrations.Tenant;

/// <summary>
///     A tenant with a database of its own that a migration run did not touch, and why.
/// </summary>
/// <remarks>
///     ⚠️ The reason is a sentence and not an enum, deliberately: what an operator does about it depends
///     on the tenant's state, and a closed set here would either grow a member per state or lose the
///     one thing that distinguishes "still being onboarded" from "suspended after a failure". It is
///     written to be read in a deployment's output.
/// </remarks>
/// <param name="TenantId">The tenant, as its register knows it.</param>
/// <param name="ConnectionString">
///     Its database, so a report can name what was not migrated rather than only who owns it.
/// </param>
/// <param name="Reason">Why the run did not reach it — the tenant's state, or that the run stopped.</param>
public sealed record TenantNotVisited(string TenantId, string? ConnectionString, string Reason);
