using Casework.Intake.Enums;
using Npgsql;
using Pragmatic.MultiTenancy;

namespace Casework.Intake.Host;

/// <summary>
///     The tenant store of this service: the organisations, read from the shared database.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>It opens the shared connection itself, and that is the whole point.</b> The framework
///         asks this store which database a tenant's rows are in
///         (<c>TenantConnectionResolver.ResolveAsync</c>), so it cannot be answered <em>through</em> the
///         tenant-routed <c>DbContext</c> — the interceptor would need the answer in order to open the
///         connection that produces it. One register, on the shared database, read with the connection
///         string the host was configured with.
///     </para>
///     <para>
///         SQL and not EF, for the same reason: a <c>DbContext</c> here is the one that routes. Every
///         method goes through <see cref="WithConnectionAsync" />, so the connection is opened and
///         disposed in one place.
///     </para>
///     <para>
///         ⚠️ The framework's stock store is <c>InMemoryTenantStore</c>, seeded from configuration and
///         written to by nothing. With it, <c>RequireKnownTenant = true</c> refuses every request and
///         <c>UseDbPerTenant</c> puts every tenant on the shared database — both silently. Registering
///         this one is what makes those two settings mean what they say.
///     </para>
/// </remarks>
internal sealed class TheOrganisationsAreTheTenants(string sharedConnectionString) : ITenantStore
{
    private const string Columns =
        """ "TenantKey", "Name", "DedicatedConnectionString", "State" """;

    public Task<TenantInfo?> GetByIdAsync(string tenantId, CancellationToken ct = default)
        => WithConnectionAsync(async connection =>
        {
            using var command = new NpgsqlCommand(
                $"""select {Columns} from "Organisations" where "TenantKey" = @key""", connection);
            command.Parameters.AddWithValue("key", tenantId);

            using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

            return await reader.ReadAsync(ct).ConfigureAwait(false) ? Read(reader) : null;
        }, ct);

    public Task<IReadOnlyList<TenantInfo>> GetAllAsync(CancellationToken ct = default)
        => QueryAsync(activeOnly: false, ct);

    public Task<IReadOnlyList<TenantInfo>> GetActiveAsync(CancellationToken ct = default)
        => QueryAsync(activeOnly: true, ct);

    /// <summary>
    ///     Registers an organisation. <c>OnboardAnOrganisationAction</c> calls it; the tests call it to set
    ///     their tenants up.
    /// </summary>
    /// <exception cref="InvalidOperationException">The tenant id is already registered.</exception>
    public async Task<TenantInfo> CreateAsync(TenantInfo tenant, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        if (await GetByIdAsync(tenant.TenantId, ct).ConfigureAwait(false) is not null)
            throw new InvalidOperationException($"The organisation '{tenant.TenantId}' is already registered.");

        await WithConnectionAsync(async connection =>
        {
            // ⚠️ No audit columns: `[Audited]` on the entity writes the change log into
            // `__AuditEntries`, it does not add `CreatedAt`/`CreatedBy` to the row — that is
            // `[Auditable]`, a different attribute with a similar name. Naming them here was a
            // "column does not exist" from PostgreSQL rather than a compile error, because this
            // statement is a string.
            using var command = new NpgsqlCommand(
                """
                insert into "Organisations"
                    ("PersistenceId", "TenantKey", "Name", "DedicatedConnectionString", "State")
                values (@id, @key, @name, @connection, @state)
                """,
                connection);
            command.Parameters.AddWithValue("id", Guid.CreateVersion7());
            command.Parameters.AddWithValue("key", tenant.TenantId);
            command.Parameters.AddWithValue("name", tenant.TenantName);
            command.Parameters.AddWithValue("connection", (object?)tenant.ConnectionString ?? DBNull.Value);
            command.Parameters.AddWithValue("state", (int)Column(tenant.State));

            return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);

        return tenant;
    }

    public Task<bool> UpdateAsync(TenantInfo tenant, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tenant);

        return WithConnectionAsync(async connection =>
        {
            using var command = new NpgsqlCommand(
                """
                update "Organisations"
                set "Name" = @name, "DedicatedConnectionString" = @connection, "State" = @state
                where "TenantKey" = @key
                """,
                connection);
            command.Parameters.AddWithValue("key", tenant.TenantId);
            command.Parameters.AddWithValue("name", tenant.TenantName);
            command.Parameters.AddWithValue("connection", (object?)tenant.ConnectionString ?? DBNull.Value);
            command.Parameters.AddWithValue("state", (int)Column(tenant.State));

            return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
        }, ct);
    }

    public Task<bool> DeactivateAsync(string tenantId, CancellationToken ct = default)
        => WithConnectionAsync(async connection =>
        {
            using var command = new NpgsqlCommand(
                """
                update "Organisations" set "State" = @state where "TenantKey" = @key
                """,
                connection);
            command.Parameters.AddWithValue("key", tenantId);
            command.Parameters.AddWithValue("state", (int)OrganisationState.Deactivated);

            return await command.ExecuteNonQueryAsync(ct).ConfigureAwait(false) > 0;
        }, ct);

    private Task<IReadOnlyList<TenantInfo>> QueryAsync(bool activeOnly, CancellationToken ct)
        => WithConnectionAsync(async connection =>
        {
            using var command = new NpgsqlCommand(
                $"""
                 select {Columns} from "Organisations"
                 {(activeOnly ? $"""where "State" = {(int)OrganisationState.Active}""" : "")}
                 order by "TenantKey"
                 """,
                connection);

            using var reader = await command.ExecuteReaderAsync(ct).ConfigureAwait(false);

            var organisations = new List<TenantInfo>();
            while (await reader.ReadAsync(ct).ConfigureAwait(false))
                organisations.Add(Read(reader));

            return (IReadOnlyList<TenantInfo>)organisations;
        }, ct);

    /// <summary>Opens the shared connection, runs <paramref name="work" /> on it, and closes it.</summary>
    private async Task<T> WithConnectionAsync<T>(
        Func<NpgsqlConnection, Task<T>> work, CancellationToken ct)
    {
        var connection = new NpgsqlConnection(sharedConnectionString);
        await using (connection.ConfigureAwait(false))
        {
            await connection.OpenAsync(ct).ConfigureAwait(false);

            return await work(connection).ConfigureAwait(false);
        }
    }

    private static TenantInfo Read(NpgsqlDataReader reader) => new()
    {
        TenantId = reader.GetString(0),
        TenantName = reader.GetString(1),
        ConnectionString = reader.IsDBNull(2) ? null : reader.GetString(2),
        State = Tenant((OrganisationState)reader.GetInt32(3)),
        CreatedAt = DateTimeOffset.UtcNow
    };

    /// <summary>
    ///     The register's column for what the framework calls a tenant state, and back.
    /// </summary>
    /// <remarks>
    ///     Two vocabularies on purpose: <c>TenantState</c> is the store contract and has words this
    ///     example never writes (<c>Migrating</c>, <c>Suspended</c>), while the column is the
    ///     organisation lifecycle the module models. ⚠️ The translation is <b>total</b> and does not fall
    ///     through to <c>Active</c>: a tenant the framework hands over mid-migration must not come back
    ///     from this register as one that may be served.
    /// </remarks>
    private static OrganisationState Column(TenantState state) => state switch
    {
        TenantState.Active => OrganisationState.Active,
        TenantState.Provisioning or TenantState.Migrating => OrganisationState.Provisioning,
        _ => OrganisationState.Deactivated
    };

    private static TenantState Tenant(OrganisationState state) => state switch
    {
        OrganisationState.Active => TenantState.Active,
        OrganisationState.Provisioning => TenantState.Provisioning,
        _ => TenantState.Deactivated
    };
}
