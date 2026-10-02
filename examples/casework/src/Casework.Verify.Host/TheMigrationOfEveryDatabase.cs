using Pragmatic.Migrations.Runner;
using Pragmatic.Migrations.Tenant;
using Pragmatic.MultiTenancy.Persistence;

namespace Casework.Verify.Host;

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
///         <b>The composition is the framework's, because both services need the same one</b> — which
///         makes it a framework concern rather than an example's business. It is
///         <c>IDatabaseMigrationSweep</c>, and what is left here is the two things the framework cannot
///         know.
///     </para>
///     <para>
///         ⚠️ <b>The schema is this host's.</b> <c>VerifyDatabaseSchema.Current</c> is generated into
///         this assembly from the modules it includes — and it is <b>not</b> Intake's, which is the
///         point of the two services having separate registers and separate databases. Every
///         organisation of this service is migrated to this schema.
///     </para>
///     <para>
///         ⚠️ <b>When it runs is this application's choice, not the framework's.</b> The generated entry
///         point also migrates at startup, which is the same two calls — so this is not a second
///         mechanism, it is the same one with the report returned instead of logged.
///     </para>
/// </remarks>
internal sealed class TheMigrationOfEveryDatabase(
    IDatabaseMigrationSweep sweep,
    TenantDatabaseOptions databases,
    MigrationOptions? options = null)
{
    public Task<IReadOnlyList<MigratedDatabase>> RunAsync(CancellationToken ct = default)
        => sweep.RunAsync(databases.DefaultConnectionString, VerifyDatabaseSchema.Current, options, ct);
}
