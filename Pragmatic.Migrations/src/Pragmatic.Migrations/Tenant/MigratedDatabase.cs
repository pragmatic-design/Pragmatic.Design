namespace Pragmatic.Migrations.Tenant;

/// <summary>
///     One line of a migration run: a database, and what happened to it.
/// </summary>
/// <remarks>
///     ⚠️ <b>The report is the deliverable, not decoration.</b> The failure mode of migrating N databases
///     is the one that says nothing: a tenant skipped because it was suspended, one whose row names a
///     database that is gone, or the rest of them after the sweep stopped at the first failure. A run
///     that answers "done" cannot be told from one that answered "done, about two of your three
///     customers" — so every database gets a line, including the ones nothing was done to.
/// </remarks>
/// <param name="Database">The database itself, by the name its connection string gives it.</param>
/// <param name="Tenant">The tenant it belongs to, or <c>null</c> for the shared one.</param>
/// <param name="Succeeded">Whether it reached the schema. True with zero changes for one left alone.</param>
/// <param name="Changes">How many statements were applied. Zero means it was already there.</param>
/// <param name="Error">What went wrong, or why it was not visited, when either applies.</param>
public sealed record MigratedDatabase(
    string Database, string? Tenant, bool Succeeded, int Changes, string? Error);
