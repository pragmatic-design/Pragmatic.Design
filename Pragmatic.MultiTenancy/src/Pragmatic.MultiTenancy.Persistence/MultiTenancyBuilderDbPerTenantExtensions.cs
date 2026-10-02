namespace Pragmatic.MultiTenancy.Persistence;

/// <summary>
///     Extension methods for adding DB-per-tenant support to <see cref="MultiTenancyBuilder"/>.
/// </summary>
public static class MultiTenancyBuilderDbPerTenantExtensions
{
    /// <summary>
    ///     Enables DB-per-tenant: tenants with dedicated connection strings get their own database.
    ///     Tenants without a connection string continue using row-level isolation on the shared database.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Wires: database <b>provisioning</b> (<see cref="ITenantDatabaseProvisioner"/>), per-tenant
    ///         <b>migration</b> orchestration, and (MT-H2) <b>automatic per-request connection routing</b>
    ///         via a <see cref="TenantConnectionInterceptor"/>. On each connection open the interceptor
    ///         rewrites the connection string to the current tenant's dedicated database; tenants without a
    ///         dedicated connection stay on the shared database (row-level isolation). No DbContext
    ///         registration change is required — EF Core auto-discovers the interceptor from DI.
    ///     </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// app.UseMultiTenancy(mt =>
    /// {
    ///     mt.UseHeader();
    ///     mt.UseDbPerTenant(db =>
    ///     {
    ///         db.DefaultConnectionString = config.GetConnectionString("Default")!;
    ///         db.ConnectionStringTemplate = "Server=localhost;Database=tenant_{0}";
    ///     });
    /// });
    /// </code>
    /// </example>
    public static MultiTenancyBuilder UseDbPerTenant(
        this MultiTenancyBuilder builder,
        Action<TenantDatabaseOptions> configure)
    {
        builder.Services.AddDbPerTenant(configure);
        return builder;
    }
}
