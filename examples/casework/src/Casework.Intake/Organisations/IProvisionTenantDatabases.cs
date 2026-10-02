using Pragmatic.Composition.Attributes;

namespace Casework.Intake.Organisations;

/// <summary>
///     Makes a database for an organisation and puts this service's schema in it.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>Declared here and implemented by the host, because the module cannot do it.</b> Applying
///         a schema needs to name the schema — <c>IntakeDatabaseSchema.Current</c> — and that constant is
///         generated into the <b>host</b>, which is the assembly that knows which modules share which
///         database. A module compiled on its own cannot see it, and asking for it at run time would be
///         a lookup for something the compiler already knows somewhere else. Decide at compile time: the module declares, the host composes.
///     </para>
///     <para>
///         The same shape as <c>ITenantStore</c>, and for the same reason: what only the host can answer
///         is declared as a contract the host fills in.
///     </para>
/// </remarks>
[ProvidedByHost(Lifetime.Singleton)]
public interface IProvisionTenantDatabases
{
    /// <summary>
    ///     Where an organisation's rows will live in this service — before anything is created.
    /// </summary>
    /// <param name="tenantId">The organisation. It ends up in the database name, and is validated here.</param>
    /// <param name="wantsItsOwnDatabase">
    ///     What the organisation asked for. <see langword="false" /> leaves it on the shared schema, and
    ///     the answer is then <see langword="null" /> — which is what the register stores for a tenant
    ///     with no database of its own, and how the connection interceptor knows to leave it alone.
    /// </param>
    /// <returns>The connection string this service will use for it, or null for the shared schema.</returns>
    /// <remarks>
    ///     ⚠️ <b>Separate from <see cref="ProvisionAsync" /> so the register can be written first.</b> The
    ///     row in <c>Provisioning</c> is what refuses requests while the work happens and what an
    ///     operator finds if the work fails; writing it after the database would mean a failure leaves no
    ///     trace at all. ⚠️ And <b>where</b> is not the caller's to decide: the template is this host's
    ///     configuration, and the other service's is not this service's business.
    /// </remarks>
    string? ConnectionStringFor(string tenantId, bool wantsItsOwnDatabase);

    /// <summary>
    ///     Creates that database if it is not there, and migrates it to this service's current schema.
    /// </summary>
    /// <param name="tenantId">The organisation it belongs to, for the message when it goes wrong.</param>
    /// <param name="connectionString">What <see cref="ConnectionStringFor" /> answered.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <remarks>Idempotent, because onboarding is not transactional and can be retried.</remarks>
    /// <exception cref="InvalidOperationException">
    ///     The database could not be made or the migration did not succeed. Deliberately an exception:
    ///     the caller cannot carry on with half a database, and the failure has to reach whoever asked —
    ///     an operator over HTTP, or the broker's dead letter queue for the service that heard a message.
    /// </exception>
    Task ProvisionAsync(string tenantId, string connectionString, CancellationToken ct = default);
}
