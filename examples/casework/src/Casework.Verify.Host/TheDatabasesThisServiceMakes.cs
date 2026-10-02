using Casework.Verify.Organisations;
using Pragmatic.Migrations.Runner;
using Pragmatic.MultiTenancy.Persistence;

namespace Casework.Verify.Host;

/// <summary>
///     Gives an organisation a database in this service, with this service's schema in it.
/// </summary>
/// <remarks>
///     <para>
///         The host's half of <see cref="IProvisionTenantDatabases" />, and it lives here for two
///         reasons. <c>VerifyDatabaseSchema.Current</c> is generated into <b>this</b> assembly, because this
///         is where it is decided which modules share which database; and the template a tenant's
///         connection string is built from is this host's configuration. A module cannot see either, and
///         asking for them at run time would be looking up what the compiler already knows somewhere
///         else: decide at compile time — the module declares, the host composes.
///     </para>
///     <para>
///         <c>TenantDatabaseOptions</c> is injected bare, which is a preference and not a requirement:
///         <c>AddDbPerTenant</c> registers the configured instance <b>and</b> an
///         <c>IOptions&lt;TenantDatabaseOptions&gt;</c> wrapping that same object, so either way of
///         asking gets it. ⚠️ Both registrations matter: with only the bare form, asking the .NET way
///         resolves a brand new instance with an empty template — no error, and a connection string with
///         no database in it, reported as "Cannot extract database name from connection string", four
///         layers from the registration that caused it. <c>BuildConnectionString</c> refuses an empty
///         template itself, naming what to set.
///     </para>
///     <para>
///         Two framework services, one after the other, and neither does the other's job: the provisioner
///         issues <c>CREATE DATABASE</c> and <b>nothing else</b> — no schema — and the migration runner
///         brings an existing database to a schema. Both are idempotent, which is what makes a retried
///         onboarding safe. ⚠️ An option once promised to do both on first access and did neither; it was
///         removed, and this class is the composition that replaces it — which cannot live in
///         the framework, because the schema constant above is generated into this assembly.
///     </para>
/// </remarks>
internal sealed class TheDatabasesThisServiceMakes(
    ITenantDatabaseProvisioner provisioner,
    IMigrationRunner migrations,
    TenantDatabaseOptions databases,
    MigrationOptions? options = null) : IProvisionTenantDatabases
{
    public string? ConnectionStringFor(string tenantId, bool wantsItsOwnDatabase)
        // Nothing to make: its rows go in the shared database beside everybody else's, and the register
        // stores no connection string for it.
        //
        // ⚠️ BuildConnectionString and not string.Format: it refuses a tenant id with anything but
        // letters, digits, '-' and '_' before it interpolates, and what arrives here came from a request.
        => wantsItsOwnDatabase ? databases.BuildConnectionString(tenantId) : null;

    public async Task ProvisionAsync(
        string tenantId, string connectionString, CancellationToken ct = default)
    {
        await provisioner.ProvisionAsync(tenantId, connectionString, ct).ConfigureAwait(false);

        var result = await migrations
            .MigrateAsync(
                new MigrationContext(
                    connectionString, VerifyDatabaseSchema.Current, options ?? new MigrationOptions()),
                ct)
            .ConfigureAwait(false);

        // ⚠️ Thrown, not returned: the caller would be left with an organisation that is registered and
        // has no tables, and nothing sensible to do about it. Over HTTP this is the operator's answer;
        // in the other service it is what sends the message to the dead letter queue instead of
        // reporting a readiness that is not there.
        if (!result.Success)
            throw new InvalidOperationException(
                $"The database for organisation '{tenantId}' was not migrated: {result.Error}");
    }
}
