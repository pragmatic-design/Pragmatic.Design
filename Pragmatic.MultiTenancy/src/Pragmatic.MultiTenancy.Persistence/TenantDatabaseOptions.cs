namespace Pragmatic.MultiTenancy.Persistence;

/// <summary>
///     Configuration for DB-per-tenant connection string resolution.
/// </summary>
/// <remarks>
///     ⚠️ <b>Nothing here provisions anything.</b> There is no "create the database and run
///     migrations on first access": the first request for a tenant whose dedicated database does not
///     exist fails with the driver's own <c>3D000: database does not exist</c>.
///     <para>
///         Creating a tenant's database is <c>ITenantDatabaseProvisioner</c>, opted into with
///         <c>UseAutoProvision&lt;T&gt;()</c> and <b>called by the application</b>; putting this
///         service's schema in it is <c>IMigrationRunner</c>. The two are composed by the application
///         and not by the framework, because the schema to migrate <em>to</em> is generated into the
///         host assembly — <c>{Database}Schema.Current</c> — and neither this type nor the provisioner
///         can name it.
///     </para>
/// </remarks>
public sealed class TenantDatabaseOptions
{
    /// <summary>
    ///     Connection string used when a tenant has no dedicated database (shared DB, row-level isolation).
    /// </summary>
    public string DefaultConnectionString { get; set; } = "";

    /// <summary>
    ///     Template for generating connection strings for new tenants.
    ///     Use <c>{0}</c> as placeholder for the tenant ID.
    ///     Example: <c>"Server=localhost;Database=tenant_{0};..."</c>.
    /// </summary>
    /// <remarks>
    ///     The tenant ID interpolated into this template must be validated before use
    ///     (e.g. alphanumeric + underscore only) to prevent format-character exceptions
    ///     and downstream SQL injection when the resulting string is used as a connection string.
    ///     Use <see cref="BuildConnectionString"/> which validates the tenant ID before formatting.
    /// </remarks>
    public string ConnectionStringTemplate { get; set; } = "";

    /// <summary>
    ///     Builds a connection string from <see cref="ConnectionStringTemplate"/> for the given
    ///     tenant ID. Validates that the tenant ID contains only safe characters before formatting.
    /// </summary>
    /// <param name="tenantId">The tenant identifier to substitute into the template.</param>
    /// <returns>The formatted connection string.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Thrown if <see cref="ConnectionStringTemplate"/> is empty — formatting one produces a
    ///     connection string with no database in it, which fails somewhere else entirely.
    /// </exception>
    /// <exception cref="ArgumentException">Thrown if <paramref name="tenantId"/> is empty or contains unsafe characters.</exception>
    public string BuildConnectionString(string tenantId)
    {
        // ⚠️ A TenantDatabaseOptions with no template is never valid, and formatting one produces a
        // connection string with no database in it — which fails far away, in whoever opens it, saying
        // nothing about where it came from. Measured as "Cannot extract database name from connection
        // string for tenant 'wayland'" thrown by the provisioner, four layers from the registration that
        // caused it. The failure belongs here, naming what to set.
        if (string.IsNullOrWhiteSpace(ConnectionStringTemplate))
            throw new InvalidOperationException(
                $"{nameof(TenantDatabaseOptions)}.{nameof(ConnectionStringTemplate)} is empty, so there is "
                + $"no connection string to build for tenant '{tenantId}'. Set it in AddDbPerTenant — and "
                + "if it was set there, check that whoever read these options asked for the registered "
                + "instance rather than building one.");

        if (string.IsNullOrWhiteSpace(tenantId))
            throw new ArgumentException("Tenant ID must not be empty.", nameof(tenantId));

        // Reject characters that could interfere with format specifiers or connection string syntax.
        if (!System.Text.RegularExpressions.Regex.IsMatch(tenantId, @"^[a-zA-Z0-9_\-]+$"))
            throw new ArgumentException($"Tenant ID '{tenantId}' contains characters that are not allowed in a connection string template.", nameof(tenantId));

        return string.Format(ConnectionStringTemplate, tenantId);
    }

    /// <summary>
    ///     Maximum number of DbContextOptions to cache per-tenant.
    ///     Prevents unbounded memory growth with many tenants.
    ///     Default: 100.
    /// </summary>
    public int OptionsPoolSize { get; set; } = 100;

    /// <summary>
    ///     How long to cache a resolved tenant connection string before re-checking the store.
    ///     Default: 5 minutes.
    /// </summary>
    public TimeSpan CacheTtl { get; set; } = TimeSpan.FromMinutes(5);
}
