using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Schema;

namespace Pragmatic.Migrations.Tenant;

/// <summary>
///     Brings <b>every</b> database an application owns to a schema — the shared one and each tenant
///     with a database of its own — and returns a line about each.
/// </summary>
/// <remarks>
///     <para>
///         The composition of the two pieces that answer different questions:
///         <see cref="Runner.IMigrationRunner" /> brings <b>one</b> database to a schema, used for the
///         shared one, which belongs to no tenant and which the sweep therefore never visits; and
///         <see cref="ITenantMigrationOrchestrator" /> sweeps the dedicated ones. Both applications that
///         needed this wrote the same twenty lines, including the part that queries the
///         register for tenants the sweep does not visit.
///     </para>
///     <para>
///         ⚠️ <b>Where and when it runs is the application's.</b> There is no command and no host mode:
///         the moment to migrate N databases belongs to a deployment — a job, an init container, a
///         one-shot run of the image, startup — and a framework that picks one is deciding something
///         that is not its to decide. What is here is <em>what</em> to
///         do, so that choosing <em>when</em> is a few lines rather than twenty.
///     </para>
///     <para>
///         ⚠️ <c>TenantMigrationOptions.ContinueOnFailure</c> applies only to the <b>sequential</b>
///         sweep. With <c>MaxParallelism &gt; 1</c> every tenant is attempted whatever it says, so a
///         report from a parallel run has no "stopped early" lines and a failure does not protect the
///         tenants after it.
///     </para>
/// </remarks>
[global::Pragmatic.Composition.Attributes.ProvidedByHost(
    global::Pragmatic.Composition.Attributes.Lifetime.Singleton)]
public interface IDatabaseMigrationSweep
{
    /// <summary>
    ///     Migrates the shared database and every tenant database, and reports on each.
    /// </summary>
    /// <param name="sharedConnectionString">
    ///     The database that belongs to no tenant — the one holding the register itself. Migrated first,
    ///     because a tenant read afterwards would otherwise be answered by a database that is behind.
    /// </param>
    /// <param name="desiredSchema">
    ///     The schema every database is brought to. ⚠️ One schema, many databases: it is generated into
    ///     the host from the modules it includes, so a tenant with a schema of its own is a different
    ///     application and not a configuration of this one — which is also why this is an argument and
    ///     not something the framework can look up.
    /// </param>
    /// <param name="options">Runner options, or <c>null</c> for the defaults.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    ///     One line per database, including the ones nothing was done to and why — a run that reports
    ///     only what it reached is indistinguishable from one that reached everything.
    /// </returns>
    Task<IReadOnlyList<MigratedDatabase>> RunAsync(
        string sharedConnectionString,
        SchemaVersion desiredSchema,
        MigrationOptions? options = null,
        CancellationToken ct = default);
}
