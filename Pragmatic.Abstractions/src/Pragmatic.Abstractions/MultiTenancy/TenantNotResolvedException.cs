namespace Pragmatic.MultiTenancy;

/// <summary>
///     Thrown when a row of an <see cref="ITenantEntity" /> would be written with no tenant resolved and
///     <c>MultiTenancyOptions.RequireTenant</c> is on.
/// </summary>
/// <remarks>
///     <para>
///         ⚠️ <b>The read side has always been fail-closed and the write side was not</b>, and a consumer
///         is all writes. A message that arrived with no tenant restored into its scope was written
///         anyway: the connection interceptor found no tenant and left the write on the <b>shared</b>
///         database, and the tenant interceptor stamped nothing — a row belonging to nobody, in
///         everybody's database, and invisible afterwards to the service's own API, whose reads are
///         fail-closed. Measured on a two-service application with a database per tenant;
///         a shared-schema application never notices, because an empty tenant column is the only trace.
///     </para>
///     <para>
///         An application that deliberately writes without a tenant — a seed, a CLI, background work
///         with a system tenant — sets <c>MultiTenancyOptions.RequireTenant</c> to
///         <see langword="false" />, which is what that option is documented for. The exception names it
///         so the one line to change is in the message.
///     </para>
///     <para>
///         ⚠️ It lives here, beside the marker interfaces, and not in <c>Pragmatic.MultiTenancy</c> with
///         the options: the interceptor that throws it is in <c>Pragmatic.Persistence.EFCore</c>, which
///         the MultiTenancy package references — naming it from there would be a cycle. This assembly is
///         what both of them can see.
///     </para>
/// </remarks>
public sealed class TenantNotResolvedException : InvalidOperationException
{
    /// <summary>Creates the exception for the entity type whose row would have had no owner.</summary>
    /// <param name="entityTypeName">The <see cref="ITenantEntity" /> being written.</param>
    public TenantNotResolvedException(string entityTypeName)
        : base($"Refusing to write a '{entityTypeName}' with no resolved tenant: the row would belong to "
               + "nobody and, with a database per tenant, would land on the shared one — where this "
               + "service's own reads could never see it again. Resolve a tenant for this scope (a "
               + "message carries it in a transport header; a job or a CLI opens one with "
               + "TenantScope.BeginScope), or set MultiTenancyOptions.RequireTenant = false if this "
               + "application writes without tenants on purpose.")
        => EntityTypeName = entityTypeName;

    /// <summary>The entity type the refused write was about.</summary>
    public string EntityTypeName { get; }
}
