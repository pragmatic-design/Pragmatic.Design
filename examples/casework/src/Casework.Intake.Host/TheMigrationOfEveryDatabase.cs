using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Tenant;
using Pragmatic.MultiTenancy.Persistence;

namespace Casework.Intake.Host;

/// <summary>
///     Brings every database this service owns to the current schema: the shared one and each
///     organisation that has its own.
/// </summary>
/// <remarks>
///     <para>
///         <b>One run, N databases, and a line about each.</b> A deployment cannot depend on remembering
///         how many customers there are, so nothing here takes a list: the register is the list, and it
///         is the same register the request pipeline reads.
///     </para>
///     <para>
///         <b>The composition is the framework's, because both services need the same one.</b> Shared
///         database through <c>IMigrationRunner</c>, dedicated ones through
///         <c>ITenantMigrationOrchestrator</c>, a line per result, and a query against the register for
///         the organisations the sweep never visits — that is <c>IDatabaseMigrationSweep</c>. What is
///         left here is the two things the framework cannot know.
///     </para>
///     <para>
///         ⚠️ <b>The schema is one object, and the databases are many.</b> <c>IntakeDatabaseSchema.Current</c>
///         is generated into this host from the modules it includes, and the build writes it to
///         <c>schema/IntakeDatabase.schema.json</c> — <b>one file per host</b>, not one per tenant. Every
///         organisation of this service is migrated to that same schema; one with a schema of its own is
///         a different application, not a configuration of this one. That is why the schema is an
///         argument to the sweep and this class exists to supply it.
///     </para>
///     <para>
///         ⚠️ <b>When it runs is this application's choice, not the framework's.</b> Here it is called by
///         a test; a deployment would call it from a job, an init container, or a one-shot run of this
///         image. The generated entry point also migrates at startup, which is the same two calls — so
///         this is not a second mechanism, it is the same one with the report returned instead of
///         logged, which is what makes it assertable.
///     </para>
/// </remarks>
internal sealed class TheMigrationOfEveryDatabase(
    IDatabaseMigrationSweep sweep,
    TenantDatabaseOptions databases,
    MigrationOptions? options = null)
{
    public Task<IReadOnlyList<MigratedDatabase>> RunAsync(CancellationToken ct = default)
        => sweep.RunAsync(databases.DefaultConnectionString, IntakeDatabaseSchema.Current, options, ct);
}
