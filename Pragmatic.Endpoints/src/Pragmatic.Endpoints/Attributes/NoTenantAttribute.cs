namespace Pragmatic.Endpoints.Attributes;

/// <summary>
///     Declares that this operation runs in no tenant: a request that resolves a tenant is refused with 400 <c>TENANT_NOT_ALLOWED</c>,
///     and one that does not runs with no tenant (#724).
/// </summary>
/// <remarks>
///     <para>
///         For platform operations: creating or listing tenants, an operator's maintenance, a report across every tenant. Under
///         <see cref="TenantAgnosticAttribute" /> a caller who brings a tenant is served in it, so its queries are filtered to that tenant
///         and its writes stamped with it; here the caller is told the operation belongs to no tenant.
///     </para>
///     <para>
///         The tenant is refused whatever resolves it, the caller's tenant claim included: an operator signed in to a tenant calls a
///         platform operation with a token that names none. Declaring it beside <c>[TenantAgnostic]</c> is PRAG0556, since the two answer
///         a supplied tenant in opposite ways.
///     </para>
///     <example>
///         <code>
/// [Endpoint(HttpVerb.Post, "api/platform/tenants")]
/// [NoTenant]
/// [RequirePermission(PlatformPermissions.Tenants.Create)]
/// public partial class CreateTenantAction : DomainAction&lt;Guid&gt; { … }
///         </code>
///     </example>
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NoTenantAttribute : Attribute;
